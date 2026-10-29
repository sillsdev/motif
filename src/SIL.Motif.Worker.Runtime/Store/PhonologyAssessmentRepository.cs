using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>The retained check exists but holds no example with the requested id.</summary>
/// <param name="message">The refusal text for the caller.</param>
public sealed class PhonologyExampleNotFoundException(string message) : KeyNotFoundException(message);

/// <summary>Reads immutable phonology evidence without opening a language project or invoking an Assessor.</summary>
public sealed class PhonologyAssessmentRepository(MotifDatabase database)
{
    private static readonly JsonSerializerOptions JsonOptions = PhonologyJson.CreateOptions();
    public const int MaxDetailUtf8Bytes = 32 * 1024 * 1024;

    public PhonologyAssessmentHeader ReadHeader(string assessmentId)
    {
        using var connection = database.OpenConnection();
        return ReadHeader(connection, assessmentId);
    }

    public PhonologyResultsPageResponse ReadPage(string assessmentId, int pageSize, PhonologyPageCursor? continuation)
    {
        PhonologyExampleValidation.ValidatePageSize(pageSize);
        if (continuation is not null && (continuation.OwnerId != assessmentId || continuation.AfterOrdinal < 0))
            throw new ArgumentException("The result cursor belongs to another Assessment or is invalid.");
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var header = ReadHeader(connection, assessmentId, transaction);
        if (continuation?.AfterOrdinal >= header.ExampleSet.Revision.ExampleCount)
            throw new ArgumentException("The result cursor is outside this Assessment.");
        using var command = Command(connection, transaction,
            "SELECT Ordinal, SummaryJson, ExampleId FROM PhonologyAssessmentRows WHERE AssessmentId = $id AND Ordinal > $after " +
            "ORDER BY Ordinal LIMIT $limit;", ("$id", assessmentId), ("$after", continuation?.AfterOrdinal ?? -1),
            ("$limit", pageSize + 1));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var rows = new List<PhonologyExampleSummary>();
        var next = (continuation?.AfterOrdinal ?? -1) + 1;
        while (reader.Read())
        {
            if (reader.GetInt32(0) != next++) throw new InvalidDataException("Result ordinals are not contiguous.");
            var row = Decode<PhonologyExampleSummary>(reader.GetString(1));
            if (row.Ordinal != reader.GetInt32(0) || row.Example.Id != reader.GetString(2)) throw new InvalidDataException("Result summary ordinal differs.");
            rows.Add(row);
        }
        var items = rows.Take(pageSize).ToList();
        var hasMore = next < header.ExampleSet.Revision.ExampleCount || rows.Count > items.Count;
        if (items.Count == 0 && next < header.ExampleSet.Revision.ExampleCount)
            throw new InvalidDataException("The Assessment is missing result rows.");
        while (true)
        {
            var cursor = hasMore && items.Count > 0 ? new PhonologyPageCursor(assessmentId, items[^1].Ordinal) : null;
            var page = new PhonologyResultsPageResponse(header, items.ToArray(), cursor);
            if (Size(page) <= PhonologyExampleValidation.MaxSerializedPageUtf8Bytes) return page;
            if (items.Count <= 1) throw new InvalidDataException("One result summary exceeds the page bound.");
            items.RemoveAt(items.Count - 1);
            hasMore = true;
        }
    }

    public PhonologyExampleResultResponse ReadExample(string assessmentId, string exampleId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        var header = ReadHeader(connection, assessmentId, transaction);
        using var command = Command(connection, transaction,
            "SELECT Ordinal, EvidenceJson FROM PhonologyAssessmentRows WHERE AssessmentId = $id AND ExampleId = $example;",
            ("$id", assessmentId), ("$example", exampleId));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) throw new PhonologyExampleNotFoundException("The retained example does not exist.");
        var detail = Decode<PhonologyExampleDetail>(reader.GetString(1));
        if (detail.Ordinal != reader.GetInt32(0) || detail.Example.Id != exampleId)
            throw new InvalidDataException("The retained example identity differs from its row binding.");
        var result = new PhonologyExampleResultResponse(header, detail);
        if (Size(result) > MaxDetailUtf8Bytes) throw new InvalidDataException("The retained example exceeds the detail bound.");
        return result;
    }

    public IReadOnlyList<PhonologyDependency> ReadDependencies(string assessmentId)
    {
        using var connection = database.OpenConnection();
        using var transaction = connection.BeginTransaction(deferred: true);
        ReadHeader(connection, assessmentId, transaction);
        return ReadDependencies(connection, assessmentId, transaction);
    }

    internal static AssessmentRecordDetails.PhonologyExamples ReadDetails(SqliteConnection connection, string assessmentId,
        SqliteTransaction? transaction = null)
    {
        var header = ReadHeader(connection, assessmentId, transaction);
        using var command = Command(connection, transaction,
            "SELECT Ordinal, EvidenceJson, ExampleId FROM PhonologyAssessmentRows WHERE AssessmentId = $id ORDER BY Ordinal;",
            ("$id", assessmentId));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var rows = new List<PhonologyExampleDetail>();
        while (reader.Read())
        {
            if (reader.GetInt32(0) != rows.Count) throw new InvalidDataException("Result ordinals are not contiguous.");
            var detail = Decode<PhonologyExampleDetail>(reader.GetString(1));
            if (detail.Ordinal != rows.Count || detail.Example.Id != reader.GetString(2))
                throw new InvalidDataException("The retained example identity differs from its row binding.");
            rows.Add(detail);
        }
        if (rows.Count != header.ExampleSet.Revision.ExampleCount) throw new InvalidDataException("The retained result count differs.");
        return new AssessmentRecordDetails.PhonologyExamples(header, rows, ReadDependencies(connection, assessmentId, transaction));
    }

    internal static void Insert(SqliteConnection connection, SqliteTransaction transaction,
        NewAssessmentRecord record, AssessmentRecordMaterial.PhonologyExamples material)
    {
        var revision = ((AssessmentSubject.PhonologyExamples)record.Subject).Revision;
        var scope = ScopeCodec.ReadPhonologyExamples(record.ScopeJson, record.Kind);
        if (scope.Revision != revision || scope.Limits != material.RequestedLimits ||
            record.BaselineToken != JsonSerializer.Serialize(material.Source.BaselineToken, SIL.Motif.Contract.MotifJson.CreateOptions()))
            throw new ArgumentException("The phonology subject, scope or Baseline source differs.");
        var frozen = PhonologyExampleSetRepository.ReadFrozen(connection, transaction, revision);
        if (record.ProposalId is { } proposal && proposal.Value != frozen.Header.ProposalId)
            throw new ArgumentException("The supporting Proposal differs from the immutable revision's association.");
        PhonologyInvocationBinding binding;
        IReadOnlyList<PhonologyMeasuredRow>? measured = null;
        IReadOnlyList<PhonologyGrammarDependency> dependencies = [];
        switch (material.Outcome)
        {
            case PhonologyAssessmentOutcome.Produced produced:
                var measurement = produced.Measurement;
                if (measurement.Limits != material.RequestedLimits || measurement.Rows.Count != revision.ExampleCount ||
                    Hash(measurement.GrammarSha256) != Hash(record.GrammarSourceSha256))
                    throw new ArgumentException("The producer evidence does not match the recorded request.");
                measured = measurement.Rows;
                dependencies = measurement.GrammarDependencies;
                binding = new PhonologyInvocationBinding.Produced(Hash(measurement.GrammarSha256),
                    Hash(measurement.RequestFileSha256), Hash(measurement.ProducerRequestSha256), measurement.ProducerBuild,
                    Hash(measurement.ExecutableSha256), Hash(measurement.OutputSha256), measurement.OutputBytes,
                    measurement.MeasuredEntryCount, measured.Count(row => row.Disposition == PhonologyDisposition.Agree),
                    measured.Count(row => row.Disposition == PhonologyDisposition.Disagree),
                    measured.Count(row => row.Disposition == PhonologyDisposition.Unavailable));
                break;
            case PhonologyAssessmentOutcome.Unavailable unavailable:
                if (!Enum.IsDefined(unavailable.Reason) || string.IsNullOrWhiteSpace(unavailable.Detail))
                    throw new ArgumentException("An unavailable invocation requires a typed reason and detail.");
                binding = new PhonologyInvocationBinding.Unavailable(unavailable.Reason, unavailable.Detail);
                break;
            default: throw new ArgumentException("Unknown phonology outcome.");
        }
        using var saved = Command(connection, transaction, "SELECT SavedUtc FROM Assessments WHERE AssessmentId = $id;",
            ("$id", record.AssessmentId));
        var header = new PhonologyAssessmentHeader(record.AssessmentId, record.Assessor, (string)saved.ExecuteScalar()!,
            frozen.Header, material.Source, material.RequestedLimits, binding);
        using (var command = Command(connection, transaction,
            "INSERT INTO PhonologyAssessmentBindings (AssessmentId, RevisionId, HeaderJson) VALUES ($id, $revision, $header);",
            ("$id", record.AssessmentId), ("$revision", revision.RevisionId), ("$header", Encode(header))))
            command.ExecuteNonQuery();
        for (var ordinal = 0; ordinal < frozen.Examples.Count; ordinal++)
        {
            var authored = frozen.Examples[ordinal];
            PhonologyRowEvidence evidence;
            PhonologyRowSummary summary;
            if (measured is not null)
            {
                var row = measured[ordinal];
                if (row.Id != authored.Id || row.Underlying != authored.Underlying ||
                    !row.Expected.SequenceEqual(authored.Expected, StringComparer.Ordinal) || row.RequestedContext != authored.Context)
                    throw new ArgumentException("The producer row differs from the exact authored example or order.");
                var produced = Map(row);
                evidence = produced;
                summary = Summarize(produced);
            }
            else
            {
                var unavailable = (PhonologyInvocationBinding.Unavailable)binding;
                evidence = new PhonologyRowEvidence.Unavailable(unavailable.Reason, unavailable.Detail);
                summary = PhonologyRowSummary.Unavailable.For(unavailable.Reason, unavailable.Detail);
            }
            var detail = new PhonologyExampleDetail(ordinal, authored, evidence);
            var shortRow = new PhonologyExampleSummary(ordinal, authored, summary);
            if (Size(new PhonologyExampleResultResponse(header, detail)) > MaxDetailUtf8Bytes ||
                Size(new PhonologyResultsPageResponse(header, [shortRow], new PhonologyPageCursor(record.AssessmentId, ordinal))) >
                    PhonologyExampleValidation.MaxSerializedPageUtf8Bytes)
                throw new ArgumentException("The retained phonology evidence exceeds a serialized response bound.");
            using var insert = Command(connection, transaction,
                "INSERT INTO PhonologyAssessmentRows (AssessmentId, RevisionId, Ordinal, ExampleId, EvidenceJson, SummaryJson) " +
                "VALUES ($id, $revision, $ordinal, $example, $evidence, $summary);", ("$id", record.AssessmentId),
                ("$revision", revision.RevisionId), ("$ordinal", ordinal), ("$example", authored.Id),
                ("$evidence", Encode(detail)), ("$summary", Encode(shortRow)));
            insert.ExecuteNonQuery();
        }
        for (var ordinal = 0; ordinal < dependencies.Count; ordinal++)
        {
            var dependency = dependencies[ordinal];
            using var insert = Command(connection, transaction,
                "INSERT INTO PhonologyAssessmentDependencies (AssessmentId, Ordinal, Path, Sha256) VALUES ($id, $ordinal, $path, $sha);",
                ("$id", record.AssessmentId), ("$ordinal", ordinal), ("$path", dependency.Path), ("$sha", Hash(dependency.Sha256)));
            insert.ExecuteNonQuery();
        }
    }

    internal static PhonologyRowEvidence.Produced Map(PhonologyMeasuredRow row) => new(
        row.RequestedContext, row.ResolvedContext, row.Underlying, row.Expected, row.EffectiveLimits,
        Enum.Parse<PhonologyRowDisposition>(row.Disposition.ToString()), Enum.Parse<PhonologyRowCompletion>(row.Completion.ToString()),
        row.ExpectedObserved, row.Observed, row.Reason is { } reason ? Enum.Parse<PhonologyRowReason>(reason.ToString()) : null,
        row.Steps, row.WorkSteps, row.Capped, row.TimedOut, row.OrderedRuleIds, row.VisitedRuleIds, row.AppliedRuleIds);

    internal static PhonologyRowSummary.Produced Summarize(PhonologyRowEvidence.Produced row) => new(
        row.RequestedContext, row.ResolvedContext, row.Underlying, row.Expected, row.EffectiveLimits, row.Disposition,
        row.Completion, row.ExpectedObserved, row.Observed, row.Reason, row.Steps, row.WorkSteps, row.Capped, row.TimedOut,
        row.OrderedRuleIds.Count, row.VisitedRuleIds.Count, row.AppliedRuleIds.Count, row.AppliedRuleIds.Take(32).ToArray(),
        Math.Max(0, row.AppliedRuleIds.Count - 32));

    private static PhonologyAssessmentHeader ReadHeader(SqliteConnection connection, string assessmentId,
        SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, transaction,
            "SELECT b.HeaderJson FROM PhonologyAssessmentBindings b JOIN Assessments a ON a.AssessmentId = b.AssessmentId " +
            "WHERE b.AssessmentId = $id AND a.SubjectKind = 'phonologyExamples' AND a.Kind = 'PhonologyExamples';",
            ("$id", assessmentId));
        RepositoryReadCounters.QueryExecuted();
        var json = command.ExecuteScalar() as string ?? throw new KeyNotFoundException("The phonology Assessment does not exist.");
        return Decode<PhonologyAssessmentHeader>(json);
    }

    private static IReadOnlyList<PhonologyDependency> ReadDependencies(SqliteConnection connection, string assessmentId,
        SqliteTransaction? transaction = null)
    {
        using var command = Command(connection, transaction,
            "SELECT Ordinal, Path, Sha256 FROM PhonologyAssessmentDependencies WHERE AssessmentId = $id ORDER BY Ordinal;",
            ("$id", assessmentId));
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        var rows = new List<PhonologyDependency>();
        while (reader.Read())
        {
            if (reader.GetInt32(0) != rows.Count) throw new InvalidDataException("Dependency ordinals are not contiguous.");
            RepositoryReadCounters.RecordDeserialized();
            rows.Add(new PhonologyDependency(reader.GetString(1), reader.GetString(2)));
        }
        return rows;
    }

    private static string Hash(string hash)
    {
        var bare = hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..] : hash;
        if (bare.Length != 64 || bare.AsSpan().IndexOfAnyExcept("0123456789abcdef") >= 0)
            throw new ArgumentException("A validated SHA-256 digest is required.");
        return "sha256:" + bare;
    }

    private static string Encode<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);
    private static int Size<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions).Length;
    private static T Decode<T>(string json)
    {
        RepositoryReadCounters.RecordDeserialized();
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new JsonException("Missing evidence."); }
        catch (JsonException exception) { throw new InvalidDataException("The retained phonology evidence is invalid.", exception); }
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
