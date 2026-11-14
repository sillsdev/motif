using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Assess;

namespace SIL.Motif.Worker.Store;

/// <summary>
/// Finds the completed Trials a newer completed Trial of the same Proposal replaces (ADR 0113). A newer Trial replaces
/// an older one only when it covers at least the older one's Assessment kinds and words, so a later narrower Trial
/// never hides an Apply-ready Assessment; a Trial whose scope cannot be read is never replaced and never replaces.
/// </summary>
internal static class SupersededTrials
{
    internal sealed record Trial(string JobId, string ProposalId, string CreatedUtc, string ResultJson,
        IReadOnlySet<string> AssessmentIds, IReadOnlySet<string> Kinds, IReadOnlySet<string>? Words);

    /// <summary>The superseded completed Trials, each with the result JSON that names its Assessments.</summary>
    internal static IReadOnlyList<Trial> Find(SqliteConnection connection, SqliteTransaction transaction)
    {
        var assessments = new Dictionary<string, (string Kind, string Scope)>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT AssessmentId, Kind, ScopeJson FROM Assessments;";
            using var reader = command.ExecuteReader();
            while (reader.Read()) assessments[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
        }
        var trials = new List<Trial>();
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT JobId, CreatedUtc, InputJson, ResultJson FROM Jobs
                WHERE Kind = 'trial' AND Status = 'completed' AND ResultJson IS NOT NULL AND json_valid(ResultJson)
                  AND json_valid(InputJson) AND json_valid(json_extract(InputJson, '$.proposalJson'));
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
                if (Read(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), assessments)
                    is { } trial) trials.Add(trial);
        }
        return trials.GroupBy(trial => trial.ProposalId, StringComparer.Ordinal).SelectMany(group =>
        {
            var ordered = group.OrderBy(trial => trial.CreatedUtc, StringComparer.Ordinal)
                .ThenBy(trial => trial.JobId, StringComparer.Ordinal).ToArray();
            return ordered.Where((older, index) => ordered.Skip(index + 1).Any(newer => Covers(newer, older)));
        }).ToArray();
    }

    private static bool Covers(Trial newer, Trial older) =>
        newer.Words is { } newerWords && older.Words is { } olderWords &&
        older.Kinds.IsSubsetOf(newer.Kinds) && olderWords.IsSubsetOf(newerWords);

    private static Trial? Read(string jobId, string createdUtc, string inputJson, string resultJson,
        Dictionary<string, (string Kind, string Scope)> assessments)
    {
        try
        {
            using var input = JsonDocument.Parse(inputJson);
            var proposalJson = input.RootElement.GetProperty("proposalJson").GetString();
            using var proposal = JsonDocument.Parse(proposalJson!);
            var proposalId = proposal.RootElement.GetProperty("proposalId").GetString();
            using var result = JsonDocument.Parse(resultJson);
            var ids = result.RootElement.GetProperty("assessmentIds").EnumerateArray()
                .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal);
            // A result naming an Assessment that is gone is not a complete Trial: it replaces nothing.
            if (proposalId is null || ids.Count == 0 || ids.Any(id => !assessments.ContainsKey(id))) return null;
            HashSet<string>? words = [];
            foreach (var id in ids)
            {
                if (words is null) break;
                using var scope = JsonDocument.Parse(assessments[id].Scope);
                if (scope.RootElement.ValueKind == JsonValueKind.Object &&
                    scope.RootElement.TryGetProperty("words", out var list) && list.ValueKind == JsonValueKind.Array)
                    words.UnionWith(list.EnumerateArray().Select(item => item.GetString()!));
                else words = null;
            }
            return new Trial(jobId, proposalId, createdUtc, resultJson, ids,
                ids.Select(id => assessments[id].Kind).ToHashSet(StringComparer.Ordinal), words);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }
}
