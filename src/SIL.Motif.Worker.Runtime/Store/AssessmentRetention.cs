using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The one rule for which of a Proposal's Assessments may be removed: only those nothing else still needs.
/// </summary>
/// <remarks>
/// An Assessment is kept when a Report cites it, a pin or a retained invocation holds it, it is the project's
/// current Assessment, or a surviving Assessment names it as the measurement it replaces, transitively. Removal
/// never rewrites a surviving row: a Report keeps its frozen binding and a replacement keeps its lineage (ADR 0042),
/// and an Assessment is never detached from its Proposal, because one without a Proposal is Baseline evidence.
/// A frozen Parsimony Report cites its saved Assessment inputs. A saved Difference cites its two Assessments by id
/// but carries what it needs by value, so it keeps neither alive (ADR 0067).
/// </remarks>
internal static class AssessmentRetention
{
    private static readonly JsonSerializerOptions ParsimonyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Counts immutable phonology Example Set revisions that retain a Proposal.</summary>
    /// <param name="connection">The open connection that owns <paramref name="transaction"/>.</param>
    /// <param name="transaction">The transaction for reading retained Example Set revisions.</param>
    /// <param name="proposalId">The Proposal id to count references for.</param>
    /// <returns>The number of immutable revisions that reference the Proposal.</returns>
    public static int CountPhonologyExampleSetReferences(SqliteConnection connection, SqliteTransaction transaction,
        string proposalId)
    {
        using var command = Command(connection, transaction,
            "SELECT COUNT(*) FROM PhonologyExampleSetRevisions WHERE ProposalId = $proposalId;",
            ("$proposalId", proposalId));
        return checked((int)(long)command.ExecuteScalar()!);
    }

    /// <summary>
    /// Removes the removable Assessments recorded against <paramref name="proposalId"/>, apart from
    /// <paramref name="exceptAssessmentId"/>, inside the caller's transaction, and returns how many it removed.
    /// </summary>
    public static int SweepProposal(SqliteConnection connection, SqliteTransaction transaction, string proposalId,
        string? exceptAssessmentId)
    {
        var removable = ReadIds(connection, transaction,
            "SELECT AssessmentId FROM Assessments WHERE ProposalId = $proposalId " +
            "AND ($exceptId IS NULL OR AssessmentId != $exceptId);",
            ("$proposalId", proposalId), ("$exceptId", exceptAssessmentId));
        if (removable.Count == 0) return 0;

        removable.ExceptWith(ReadIds(connection, transaction, """
            SELECT AssessmentId FROM Reports WHERE AssessmentId IS NOT NULL
            UNION SELECT AssessmentId FROM AssessmentPins
            UNION SELECT AssessmentId FROM RetainedInvocationMembers
            UNION SELECT CurrentAssessmentId FROM MotifMetadata WHERE CurrentAssessmentId IS NOT NULL;
            """));
        removable.ExceptWith(ReadFrozenReportAssessmentIds(connection, transaction));
        var all = ReadIds(connection, transaction, "SELECT AssessmentId FROM Assessments;");
        removable.ExceptWith(ReadJsonReferences(connection, transaction,
            "SELECT InputJson FROM Jobs WHERE Kind IN ('trial', 'parsimony-report', 'parsimony-candidate') " +
            "AND Status IN ('queued', 'running', 'waiting-for-baseline', 'waiting-for-project-host');", all));
        var replacements = ReadPairs(connection, transaction,
            "SELECT AssessmentId, ReplacesAssessmentId FROM Assessments WHERE ReplacesAssessmentId IS NOT NULL;");
        for (var kept = true; kept;)
        {
            kept = false;
            foreach (var (replacement, root) in replacements)
                if (!removable.Contains(replacement) && removable.Remove(root)) kept = true;
        }
        if (removable.Count == 0) return 0;

        return DeleteAssessments(connection, transaction, removable, removeInvocations: false).Count;
    }

    /// <summary>Prunes unreferenced standalone Assessments while the project parse lease is held.</summary>
    public static int SweepStandalone(SqliteConnection connection, SqliteTransaction transaction,
        ProjectParseLease lease, int keepPerKind, IReadOnlyCollection<string> currentEvidence) =>
        SweepStandalone(connection, transaction, lease, keepPerKind, currentEvidence, out _);

    /// <summary>
    /// The newest Object Timing Assessments kept as standalone evidence: each stores a per-object timing table of
    /// about a megabyte and is read only while it is the latest, so it is capped below the general count.
    /// </summary>
    internal const int ObjectTimingsKept = 5;

    /// <summary>The Proposal statuses whose Assessments nothing still needs once they are not cited.</summary>
    private const string ClosedStatuses = "('applied', 'rejected', 'withdrawn', 'superseded')";

    /// <summary>
    /// Removes the removable standalone Assessments one at a time, checking <paramref name="cancellationToken"/> between
    /// Assessments and reporting each removal to <paramref name="progress"/>. A stopped sweep has removed whole
    /// Assessments only, so the caller may commit what it did; <paramref name="removedInvocations"/> names only the
    /// artifacts of Assessments that were removed. When <paramref name="byteBudget"/> is given and the store still
    /// holds more than that many bytes of pages in use afterwards, the oldest Assessments nothing needs are removed
    /// next, standalone or recorded against a closed Proposal, until the store fits or none is left.
    /// </summary>
    public static int SweepStandalone(SqliteConnection connection, SqliteTransaction transaction,
        ProjectParseLease lease, int keepPerKind, IReadOnlyCollection<string> currentEvidence,
        out IReadOnlyList<string> removedInvocations, CancellationToken cancellationToken = default,
        Action<string>? progress = null, long? byteBudget = null)
    {
        removedInvocations = [];
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(keepPerKind);
        ArgumentNullException.ThrowIfNull(currentEvidence);
        lease.EnsureHeld(connection, transaction);
        var all = ReadIds(connection, transaction, "SELECT AssessmentId FROM Assessments;");
        var removable = ReadIds(connection, transaction, """
            SELECT AssessmentId FROM (
                SELECT AssessmentId, ROW_NUMBER() OVER (PARTITION BY Kind ORDER BY SavedUtc DESC, AssessmentId DESC) AS rank,
                       Kind
                FROM Assessments WHERE ProposalId IS NULL
            ) WHERE rank > CASE WHEN Kind = 'ObjectTiming' THEN MIN(CAST($keep AS INTEGER), CAST($timings AS INTEGER))
                                ELSE CAST($keep AS INTEGER) END;
            """, ("$keep", keepPerKind.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("$timings", ObjectTimingsKept.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var artifacts = new List<(string Artifact, string[] Requires)>();
        var deleted = RemoveUnneeded(connection, transaction, removable, null, currentEvidence, all, artifacts,
            cancellationToken, progress, null);
        var count = deleted.Count;
        if (byteBudget is { } budget && !cancellationToken.IsCancellationRequested && BytesInUse(connection, transaction) > budget)
        {
            var oldest = ReadOrderedIds(connection, transaction, $"""
                SELECT a.AssessmentId FROM Assessments a LEFT JOIN Proposals p ON p.ProposalId = a.ProposalId
                WHERE a.ProposalId IS NULL OR p.Status IN {ClosedStatuses}
                ORDER BY a.SavedUtc, a.AssessmentId;
                """).Where(id => !deleted.Contains(id)).ToList();
            var survivors = ReadIds(connection, transaction, "SELECT AssessmentId FROM Assessments;");
            var more = RemoveUnneeded(connection, transaction, oldest.ToHashSet(StringComparer.Ordinal), oldest,
                currentEvidence, survivors, artifacts, cancellationToken, progress,
                () => BytesInUse(connection, transaction) <= budget);
            deleted.UnionWith(more);
            count += more.Count;
        }
        removedInvocations = artifacts.Where(entry => entry.Requires.All(deleted.Contains))
            .Select(entry => entry.Artifact).Distinct(StringComparer.Ordinal).ToArray();
        return count;
    }

    /// <summary>The bytes of pages the store uses, free pages inside the file not counted.</summary>
    internal static long BytesInUse(SqliteConnection connection, SqliteTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT (page_count - freelist_count) * page_size FROM pragma_page_count, pragma_freelist_count, pragma_page_size;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    // Deletes the candidates nothing needs, oldest first when ordered, and stops once satisfied.
    private static HashSet<string> RemoveUnneeded(SqliteConnection connection, SqliteTransaction transaction,
        HashSet<string> removable, IReadOnlyList<string>? order, IReadOnlyCollection<string> currentEvidence,
        HashSet<string> all, List<(string Artifact, string[] Requires)> artifacts, CancellationToken cancellationToken,
        Action<string>? progress, Func<bool>? satisfied)
    {
        if (removable.Count == 0) return [];
        removable.ExceptWith(currentEvidence);
        removable.ExceptWith(ReadIds(connection, transaction, """
            SELECT AssessmentId FROM Reports WHERE AssessmentId IS NOT NULL
            UNION SELECT AssessmentId FROM AssessmentPins
            UNION SELECT CurrentAssessmentId FROM MotifMetadata WHERE CurrentAssessmentId IS NOT NULL;
            """));
        removable.ExceptWith(ReadFrozenReportAssessmentIds(connection, transaction));
        foreach (var sql in new[]
        {
            "SELECT ProposalJson FROM ProposalRevisions;",
            "SELECT DraftJson FROM Proposals WHERE DraftJson IS NOT NULL;",
            "SELECT AnchorJson FROM Proposals WHERE AnchorJson IS NOT NULL;",
            "SELECT ReceiptJson FROM Receipts;",
            "SELECT RecordJson FROM AppliedIndex WHERE RecordJson IS NOT NULL;",
            "SELECT InputJson FROM Jobs WHERE Kind = 'trial';",
            "SELECT ResultJson FROM Jobs WHERE Kind = 'trial' AND ResultJson IS NOT NULL;",
            "SELECT InputJson FROM Jobs WHERE Kind IN ('parsimony-report', 'parsimony-candidate') " +
                "AND Status IN ('queued', 'running', 'waiting-for-baseline', 'waiting-for-project-host');",
        })
            removable.ExceptWith(ReadJsonReferences(connection, transaction, sql, all));

        var replacements = ReadPairs(connection, transaction,
            "SELECT AssessmentId, ReplacesAssessmentId FROM Assessments WHERE ReplacesAssessmentId IS NOT NULL;");
        var invocationMembers = ReadPairs(connection, transaction,
            "SELECT InvocationId, AssessmentId FROM RetainedInvocationMembers;")
            .GroupBy(pair => pair.Replacement, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.Root).ToArray(), StringComparer.Ordinal);
        var invocationReferences = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using (var command = Command(connection, transaction,
                   "SELECT InvocationId, SelectionDescriptorJson FROM RetainedInvocations;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) invocationReferences.Add(reader.GetString(0), References(reader.GetString(1), all));
        var assessmentReferences = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        using (var command = Command(connection, transaction, "SELECT AssessmentId, ScopeJson FROM Assessments;"))
        using (var reader = command.ExecuteReader())
            while (reader.Read()) assessmentReferences.Add(reader.GetString(0), References(reader.GetString(1), all));

        bool changed;
        do
        {
            changed = false;
            foreach (var (replacement, root) in replacements)
                if (!removable.Contains(replacement) && removable.Remove(root)) changed = true;
            foreach (var (assessment, references) in assessmentReferences)
                if (!removable.Contains(assessment))
                    foreach (var reference in references) if (removable.Remove(reference)) changed = true;
            foreach (var (invocation, members) in invocationMembers)
            {
                if (members.All(removable.Contains)) continue;
                foreach (var member in members) if (removable.Remove(member)) changed = true;
                if (!invocationReferences.TryGetValue(invocation, out var references))
                    throw new InvalidDataException("A retained invocation is missing its aggregate.");
                foreach (var reference in references) if (removable.Remove(reference)) changed = true;
            }
        } while (changed);
        if (removable.Count == 0) return [];
        // An artifact goes only when every Assessment needing it was removed.
        var found = new List<(string Artifact, string[] Requires)>();
        foreach (var (assessment, invocation) in ReadPairs(connection, transaction,
                     "SELECT AssessmentId, InvocationId FROM Assessments WHERE InvocationId IS NOT NULL;"))
            if (removable.Contains(assessment)) found.Add((invocation, [assessment]));
        foreach (var (assessment, path) in ReadPairs(connection, transaction,
                     "SELECT AssessmentId, CachePath FROM Assessments WHERE CachePath IS NOT NULL;"))
            if (removable.Contains(assessment) && Path.IsPathFullyQualified(path) && Path.GetDirectoryName(path) is { } directory)
                found.Add((Path.GetFullPath(directory), [assessment]));
        foreach (var (invocation, artifact) in ReadPairs(connection, transaction,
                     "SELECT InvocationId, ArtifactInvocationId FROM RetainedInvocations;"))
            if (invocationMembers.TryGetValue(invocation, out var members) && members.All(removable.Contains))
                found.Add((artifact, members));
        foreach (var (assessment, retry) in ReadPairs(connection, transaction, """
                     SELECT a.AssessmentId, json_extract(i.EvidenceJson, '$.StoppedWordRetry.RetryInvocationId')
                     FROM Assessments a JOIN AssessmentInvocations i ON a.InvocationId = i.InvocationId
                     WHERE json_extract(i.EvidenceJson, '$.StoppedWordRetry.RetryInvocationId') IS NOT NULL;
                     """))
            if (removable.Contains(assessment)) found.Add((retry, [assessment]));
        var deleted = DeleteAssessments(connection, transaction, removable, removeInvocations: true, cancellationToken,
            progress, order, satisfied);
        artifacts.AddRange(found);
        return deleted;
    }

    private static List<string> ReadOrderedIds(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    private static HashSet<string> ReadJsonReferences(SqliteConnection connection, SqliteTransaction transaction,
        string sql, HashSet<string> assessmentIds)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        using var command = Command(connection, transaction, sql);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var value = reader.GetValue(0);
            var json = value is byte[] bytes ? new UTF8Encoding(false, true).GetString(bytes) : (string)value;
            references.UnionWith(References(json, assessmentIds));
        }
        return references;
    }

    private static HashSet<string> References(string json, HashSet<string> assessmentIds)
    {
        var references = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            using var document = JsonDocument.Parse(json);
            Visit(document.RootElement);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Stored evidence has malformed JSON; its Assessment citations are unknown.", exception);
        }
        return references;

        void Visit(JsonElement value)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in value.EnumerateObject()) Visit(property.Value);
                    break;
                case JsonValueKind.Array:
                    foreach (var item in value.EnumerateArray()) Visit(item);
                    break;
                case JsonValueKind.String:
                    var text = value.GetString()!;
                    if (assessmentIds.Contains(text)) references.Add(text);
                    else if (text.Length > 1 && text[0] is '{' or '[')
                    {
                        try { using var nested = JsonDocument.Parse(text); Visit(nested.RootElement); }
                        catch (JsonException) { }
                    }
                    break;
            }
        }
    }

    /// <summary>Removes whole Assessments one by one, stopping between them on cancellation.</summary>
    private static HashSet<string> DeleteAssessments(SqliteConnection connection, SqliteTransaction transaction,
        HashSet<string> removable, bool removeInvocations, CancellationToken cancellationToken = default,
        Action<string>? progress = null, IReadOnlyList<string>? order = null, Func<bool>? satisfied = null)
    {
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        Execute(connection, transaction, "CREATE TEMP TABLE IF NOT EXISTS RemovedAssessments (AssessmentId TEXT PRIMARY KEY);");
        Execute(connection, transaction, "DELETE FROM temp.RemovedAssessments;");
        if (removeInvocations)
        {
            Execute(connection, transaction, "CREATE TEMP TABLE IF NOT EXISTS RemovedInvocations (InvocationId TEXT PRIMARY KEY);");
            Execute(connection, transaction, "DELETE FROM temp.RemovedInvocations;");
        }
        var number = 0;
        var batch = 1;
        var inBatch = 0;
        foreach (var id in order?.Where(removable.Contains) ?? removable.Order(StringComparer.Ordinal))
        {
            if (cancellationToken.IsCancellationRequested) break;
            // A budgeted sweep measures in doubling batches, each followed by one set-based payload cleanup.
            if (satisfied is not null && inBatch == batch)
            {
                RemoveUnusedPayloads(connection, transaction);
                inBatch = 0;
                batch *= 2;
            }
            if (satisfied is not null && inBatch == 0 && satisfied()) break;
            inBatch++;
            Execute(connection, transaction, "INSERT INTO temp.RemovedAssessments (AssessmentId) VALUES ($id);", ("$id", id));
            if (removeInvocations)
            {
                Execute(connection, transaction, "INSERT OR IGNORE INTO temp.RemovedInvocations SELECT InvocationId FROM RetainedInvocationMembers WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);");
                Execute(connection, transaction, "DELETE FROM RetainedInvocationMembers WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);");
                Execute(connection, transaction, "DELETE FROM RetainedInvocations WHERE InvocationId IN (SELECT InvocationId FROM temp.RemovedInvocations) AND NOT EXISTS (SELECT 1 FROM RetainedInvocationMembers m WHERE m.InvocationId = RetainedInvocations.InvocationId);");
            }
            foreach (var sql in new[]
            {
                "DELETE FROM PhonologyAssessmentDependencies WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                "DELETE FROM PhonologyAssessmentRows WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                "DELETE FROM PhonologyAssessmentBindings WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                "DELETE FROM AssessmentObjectTimings WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                """
                DELETE FROM ParsedAnalyses WHERE AssessedWordId IN (SELECT AssessedWordId FROM AssessedWords
                    WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments));
                """,
                "DELETE FROM AssessedWords WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                "DELETE FROM Assessments WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
                "DELETE FROM temp.RemovedAssessments;",
            })
                Execute(connection, transaction, sql);
            deleted.Add(id);
            progress?.Invoke($"Removed Assessment {id} ({++number} of {removable.Count}).");
        }
        // Payloads are shared by hash; a stopped sweep leaves orphans for the next one.
        if (!cancellationToken.IsCancellationRequested) RemoveUnusedPayloads(connection, transaction);
        if (removeInvocations)
            Execute(connection, transaction, "DELETE FROM AssessmentInvocations WHERE NOT EXISTS (SELECT 1 FROM Assessments a WHERE a.InvocationId = AssessmentInvocations.InvocationId) AND NOT EXISTS (SELECT 1 FROM RetainedInvocations r WHERE r.ArtifactInvocationId = AssessmentInvocations.InvocationId);");
        return deleted;
    }

    private static void RemoveUnusedPayloads(SqliteConnection connection, SqliteTransaction transaction) =>
            Execute(connection, transaction, """
                DELETE FROM AssessmentWordPayloads WHERE Hash NOT IN (
                    SELECT MorphologyJson FROM AssessedWords WHERE MorphologyJson IS NOT NULL
                    UNION SELECT CorrectnessJson FROM AssessedWords WHERE CorrectnessJson IS NOT NULL
                    UNION SELECT ReadingGradesJson FROM AssessedWords WHERE ReadingGradesJson IS NOT NULL
                    UNION SELECT AnalysisComparisonJson FROM AssessedWords WHERE AnalysisComparisonJson IS NOT NULL
                    UNION SELECT MissedApprovedJson FROM AssessedWords WHERE MissedApprovedJson IS NOT NULL);
                """);

    private static HashSet<string> ReadFrozenReportAssessmentIds(SqliteConnection connection,
        SqliteTransaction transaction)
    {
        using var command = Command(connection, transaction,
            "SELECT ReportId, Kind, AssessmentId, ReportJson, EvidenceJson FROM Reports;");
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var reportId = reader.GetString(0);
            var kind = reader.IsDBNull(1) ? null : reader.GetString(1);
            var assessmentId = reader.IsDBNull(2) ? null : reader.GetString(2);
            switch (kind)
            {
                case "parsimony":
                    try
                    {
                        ids.UnionWith(ReadParsimonyReportAssessmentIds(reportId, reader.GetString(3),
                            reader.IsDBNull(4) ? null : reader.GetString(4)));
                    }
                    catch (Exception exception) when (exception is JsonException or ArgumentException or
                                                       InvalidOperationException)
                    {
                        throw new InvalidDataException(
                            $"Parsimony Report '{reportId}' has an unsupported or malformed Assessment citation.",
                            exception);
                    }
                    break;
                case CoverageReportProducer.KindName:
                case CorrectnessReportProducer.KindName:
                case DifferenceReportProducer.KindName:
                    if (assessmentId is null)
                        throw new InvalidDataException(
                            $"Report '{reportId}' has no direct Assessment citation for kind '{kind}'.");
                    break;
                case null:
                    if (assessmentId is null && (!reader.IsDBNull(3) || !reader.IsDBNull(4)))
                        throw new InvalidDataException(
                            $"Legacy Report '{reportId}' has stored evidence without a recognized Assessment citation.");
                    break;
                default:
                    throw new InvalidDataException(
                        $"Report '{reportId}' has unrecognized kind '{kind}' and its Assessment citations are unknown.");
            }
        }
        return ids;
    }

    private static HashSet<string> ReadParsimonyReportAssessmentIds(string reportId, string reportJson,
        string? evidenceJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        RequireKnownObjectProperties(document.RootElement, "Parsimony Report",
        [
            "reportId", "inputs", "findings", "text", "assessmentIds", "measureRuns", "joinQuality",
            "dispositionProjection", "notes",
        ]);
        if (!document.RootElement.TryGetProperty("inputs", out _))
            throw new JsonException("A Parsimony Report must include its frozen inputs.");
        var report = JsonSerializer.Deserialize<ParsimonyReportResponse>(reportJson, ParsimonyJsonOptions) ??
            throw new JsonException("The Parsimony Report is empty.");
        if (report.Inputs is null) throw new JsonException("The Parsimony Report inputs are missing.");
        if (!string.Equals(report.ReportId, reportId, StringComparison.Ordinal))
            throw new JsonException("The Parsimony Report id does not match its stored row.");

        var ids = CitationIds(report.AssessmentIds, "Parsimony Report Assessment ids");
        ids.UnionWith(CitationIds(report.Inputs.AssessmentIds, "Parsimony input Assessment ids"));
        if (evidenceJson is null) throw new JsonException("Frozen Parsimony evidence is missing.");
        using var evidenceDocument = JsonDocument.Parse(evidenceJson);
        RequireKnownObjectProperties(evidenceDocument.RootElement, "frozen Parsimony evidence",
            ["inputs", "grammarFactsPath", "evidencePath", "findings", "joinQuality"]);
        var evidence = JsonSerializer.Deserialize<FrozenParsimonyEvidence>(evidenceJson, ParsimonyJsonOptions) ??
            throw new JsonException("Frozen Parsimony evidence is empty.");
        if (evidence.Inputs is null || string.IsNullOrWhiteSpace(evidence.GrammarFactsPath) ||
            string.IsNullOrWhiteSpace(evidence.EvidencePath) || evidence.Findings is null ||
            evidence.Findings.Any(finding => finding is null || string.IsNullOrWhiteSpace(finding.FindingId) ||
                string.IsNullOrWhiteSpace(finding.EvidenceDigest)))
            throw new JsonException("Frozen Parsimony evidence is incomplete.");
        ids.UnionWith(CitationIds(evidence.Inputs.AssessmentIds, "frozen Parsimony input Assessment ids"));
        return ids;
    }

    private static HashSet<string> CitationIds(IReadOnlyList<string>? candidates, string source)
    {
        if (candidates is null || candidates.Any(string.IsNullOrWhiteSpace))
            throw new JsonException($"{source} are missing or malformed.");
        return candidates.ToHashSet(StringComparer.Ordinal);
    }

    private static void RequireKnownObjectProperties(JsonElement element, string source,
        IReadOnlyCollection<string> expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{source} must be a JSON object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException($"{source} repeats '{property.Name}'.");
            if (!expectedProperties.Contains(property.Name))
                throw new JsonException($"{source} has unknown property '{property.Name}'.");
        }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenParsimonyEvidence(
        [property: JsonRequired] ParsimonyReportInputs Inputs,
        [property: JsonRequired] string GrammarFactsPath,
        [property: JsonRequired] string EvidencePath,
        [property: JsonRequired] IReadOnlyList<FrozenParsimonyFinding> Findings,
        [property: JsonRequired] ParsimonyJoinQuality? JoinQuality);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenParsimonyFinding(
        [property: JsonRequired] string FindingId,
        [property: JsonRequired] string EvidenceDigest);

    private static HashSet<string> ReadIds(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        using var reader = command.ExecuteReader();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) ids.Add(reader.GetString(0));
        return ids;
    }

    private static List<(string Replacement, string Root)> ReadPairs(SqliteConnection connection,
        SqliteTransaction transaction, string sql)
    {
        using var command = Command(connection, transaction, sql);
        using var reader = command.ExecuteReader();
        var pairs = new List<(string, string)>();
        while (reader.Read()) pairs.Add((reader.GetString(0), reader.GetString(1)));
        return pairs;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        using var command = Command(connection, transaction, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction transaction, string sql,
        params (string Name, string? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, (object?)value ?? DBNull.Value);
        return command;
    }
}

/// <summary>Indicates that immutable phonology Example Set revisions retain a Proposal.</summary>
/// <param name="revisionCount">The number of associated immutable revisions retaining the Proposal.</param>
internal sealed class ProposalRetainedByExampleSetException(int revisionCount)
    : InvalidOperationException($"This Proposal is retained by {revisionCount} immutable phonology Example Set " +
        $"revision{(revisionCount == 1 ? "" : "s")} and cannot be deleted while that reference exists.")
{
    /// <summary>The number of immutable Example Set revisions retaining the Proposal.</summary>
    internal int RevisionCount { get; } = revisionCount;
}
