using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// The one rule for which of a Proposal's Assessments may be removed: only those nothing else still needs.
/// </summary>
/// <remarks>
/// An Assessment is kept when a Report or saved Difference cites it, a pin or a retained invocation holds it, it is the project's
/// current Assessment, or a surviving Assessment names it as the measurement it replaces, transitively. Removal
/// never rewrites a surviving row: a Report keeps its frozen binding and a replacement keeps its lineage (ADR 0042),
/// and an Assessment is never detached from its Proposal, because one without a Proposal is Baseline evidence.
/// A Difference cites both compared Assessments, and a frozen Parsimony Report cites its saved Assessment inputs.
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
            UNION SELECT BeforeAssessmentId FROM ProposalDifferences
            UNION SELECT AfterAssessmentId FROM ProposalDifferences
            UNION SELECT AssessmentId FROM AssessmentPins
            UNION SELECT AssessmentId FROM RetainedInvocationMembers
            UNION SELECT CurrentAssessmentId FROM MotifMetadata WHERE CurrentAssessmentId IS NOT NULL;
            """));
        removable.ExceptWith(ReadFrozenReportAssessmentIds(connection, transaction));
        var replacements = ReadPairs(connection, transaction,
            "SELECT AssessmentId, ReplacesAssessmentId FROM Assessments WHERE ReplacesAssessmentId IS NOT NULL;");
        for (var kept = true; kept;)
        {
            kept = false;
            foreach (var (replacement, root) in replacements)
                if (!removable.Contains(replacement) && removable.Remove(root)) kept = true;
        }
        if (removable.Count == 0) return 0;

        Execute(connection, transaction, "CREATE TEMP TABLE IF NOT EXISTS RemovedAssessments (AssessmentId TEXT PRIMARY KEY);");
        Execute(connection, transaction, "DELETE FROM temp.RemovedAssessments;");
        foreach (var id in removable)
            Execute(connection, transaction, "INSERT INTO temp.RemovedAssessments (AssessmentId) VALUES ($id);", ("$id", id));
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
            """
            DELETE FROM AssessmentWordPayloads WHERE Hash NOT IN (
                SELECT MorphologyJson FROM AssessedWords WHERE MorphologyJson IS NOT NULL
                UNION SELECT CorrectnessJson FROM AssessedWords WHERE CorrectnessJson IS NOT NULL
                UNION SELECT ReadingGradesJson FROM AssessedWords WHERE ReadingGradesJson IS NOT NULL
                UNION SELECT AnalysisComparisonJson FROM AssessedWords WHERE AnalysisComparisonJson IS NOT NULL
                UNION SELECT MissedApprovedJson FROM AssessedWords WHERE MissedApprovedJson IS NOT NULL);
            """,
            "DELETE FROM Assessments WHERE AssessmentId IN (SELECT AssessmentId FROM temp.RemovedAssessments);",
            "DELETE FROM temp.RemovedAssessments;",
        })
            Execute(connection, transaction, sql);
        return removable.Count;
    }

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
