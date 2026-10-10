using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

internal static class ExpectedContextRepository
{
    public static bool IsCurrentContext(MotifDatabase database, SqliteConnection connection,
        SqliteTransaction transaction, string projectKey, ExpectedContext expected)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, CapturedUtc, BundleDigest,
                CapturedHostSessionId, CapturedEditGeneration
            FROM Baselines WHERE ProjectKey = $project;
            """;
        command.Parameters.AddWithValue("$project", projectKey);
        RepositoryReadCounters.QueryExecuted();
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return false;
        RepositoryReadCounters.RecordDeserialized();
        var current = new BaselineToken(reader.GetString(0), reader.GetString(1),
            reader.GetString(2), reader.GetString(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetInt64(6));
        reader.Close();
        return current == expected.Baseline && IsCurrentSelectionEvidence(database, connection,
            transaction, projectKey, expected);
    }

    public static bool IsCurrentSelectionEvidence(MotifDatabase database, SqliteConnection connection,
        SqliteTransaction transaction, string projectKey, ExpectedContext expected)
    {
        var evidence = expected.SelectionEvidence;
        if (evidence is null) return true;
        if (evidence.Origin is not ("default" or "explicit")) return false;

        if (evidence.Origin == "default")
        {
            var declaration = new NamedSelectionRepository(database).GetDefault(connection, transaction);
            if (declaration is null || declaration.Name != evidence.DeclarationName ||
                !declaration.TextIds.SequenceEqual(evidence.DeclarationTextIds) ||
                !declaration.AddedWords.SequenceEqual(evidence.DeclarationAddedWords, StringComparer.Ordinal) ||
                !declaration.TextIds.SequenceEqual(expected.TextIds) ||
                !declaration.AddedWords.SequenceEqual(expected.AddedWords, StringComparer.Ordinal))
                return false;
        }

        AssessmentRecord? root = null;
        IReadOnlyList<string> replacements = [];
        IReadOnlyList<string> measurements = [];
        if (evidence.AssessmentSelectionSha256 is { } selectionSha)
        {
            var repository = new AssessmentRepository(database);
            root = evidence.Origin == "explicit" && evidence.RootAssessmentId is { } namedRoot
                ? repository.GetHeader(connection, transaction, namedRoot)
                : repository.FindLatestBaselineAssessmentHeader(connection, transaction,
                    AssessmentKind.ParseTime.ToStoredKind(), BaselineTokenJson(expected), selectionSha,
                    evidence.DeclarationName ?? string.Empty);
            if (root is not null && !MatchesNamedRoot(root, expected)) return false;
            if (root is not null && Selection.Create(root.Selection.Name, root.Selection.Words).Sha256 != selectionSha)
                return false;
            if (root?.AssessmentId != evidence.RootAssessmentId) return false;
            if (root is not null)
            {
                var candidates = new AssessmentRepository(database).ReadReplacementHeaders(connection, transaction,
                    AssessmentKind.ParseTime.ToStoredKind(), BaselineTokenJson(expected), root.AssessmentId);
                var sourceWords = root.Selection.Words.ToHashSet(StringComparer.Ordinal);
                replacements = candidates.Where(candidate =>
                        string.CompareOrdinal(candidate.SavedUtc, root.SavedUtc) > 0 &&
                        candidate.Selection.Words.Count > 0 && candidate.Selection.Words.All(sourceWords.Contains))
                    .OrderBy(candidate => candidate.SavedUtc, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.AssessmentId, StringComparer.Ordinal)
                    .Select(candidate => candidate.AssessmentId).ToArray();
                measurements = ReadMeasurementIds(connection, transaction, expected, root.Invocation?.InvocationId);
            }
        }
        else if (evidence.RootAssessmentId is not null)
        {
            return false;
        }

        if (!evidence.ReplacementAssessmentIds.SequenceEqual(replacements, StringComparer.Ordinal) ||
            !evidence.MeasurementAssessmentIds.SequenceEqual(measurements, StringComparer.Ordinal))
            return false;
        var warnings = ReadWarningIdentities(connection, transaction, expected, root);
        return evidence.WarningIdentities.SequenceEqual(warnings, StringComparer.Ordinal);
    }

    internal static bool MatchesNamedRoot(AssessmentRecord root, ExpectedContext expected) =>
        expected.SelectionEvidence is { } evidence && root.ProposalId is null &&
        root.Kind == AssessmentKind.ParseTime.ToStoredKind() && root.ReplacesAssessmentId is null &&
        root.BaselineToken == BaselineTokenJson(expected) &&
        root.Selection.Name == (evidence.DeclarationName ?? string.Empty) &&
        root.Selection.Sha256 == evidence.AssessmentSelectionSha256 &&
        Selection.Create(root.Selection.Name, root.Selection.Words).Sha256 == evidence.AssessmentSelectionSha256;

    public static IReadOnlyList<string> ReadMeasurementIds(SqliteConnection connection,
        SqliteTransaction transaction, ExpectedContext expected, string? invocationId)
    {
        if (invocationId is null) return [];
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT AssessmentId, Kind FROM Assessments
            WHERE ProposalId IS NULL AND BaselineToken = $baseline AND InvocationId = $invocation
                AND Kind IN ($correctness, $timing)
            ORDER BY SavedUtc, AssessmentId;
            """;
        command.Parameters.AddWithValue("$baseline", BaselineTokenJson(expected));
        command.Parameters.AddWithValue("$invocation", invocationId);
        command.Parameters.AddWithValue("$correctness", AssessmentKind.Correctness.ToStoredKind());
        command.Parameters.AddWithValue("$timing", AssessmentKind.ObjectTiming.ToStoredKind());
        RepositoryReadCounters.QueryExecuted();
        var latest = new Dictionary<string, string>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            RepositoryReadCounters.RecordDeserialized();
            latest[reader.GetString(1)] = reader.GetString(0);
        }
        return latest.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value).ToArray();
    }

    public static IReadOnlyList<string> ReadWarningIdentities(SqliteConnection connection,
        SqliteTransaction transaction, ExpectedContext expected, AssessmentRecord? root)
    {
        var warnings = new List<string>();
        if (root?.Invocation?.GrammarWarnings is { Length: > 0 } grammarWarnings)
            warnings.Add("grammar:" + Digest(grammarWarnings));

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT RefusalJson FROM ParserRefusals WHERE BaselineToken = $baseline;";
        command.Parameters.AddWithValue("$baseline", BaselineTokenJson(expected));
        RepositoryReadCounters.QueryExecuted();
        if (command.ExecuteScalar() is string refusalJson)
        {
            RepositoryReadCounters.RecordDeserialized();
            warnings.Add("refusal:" + Digest(refusalJson));
        }
        return warnings.OrderBy(identity => identity, StringComparer.Ordinal).ToArray();
    }

    public static string Digest(string value) => "sha256:" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string BaselineTokenJson(ExpectedContext expected) =>
        JsonSerializer.Serialize(expected.Baseline, MotifJson.CreateOptions());
}
