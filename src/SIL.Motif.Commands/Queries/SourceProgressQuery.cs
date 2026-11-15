using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Sources;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads Source denominators from a published projection without rebuilding it.</summary>
public static class SourceProgressQuery
{
    /// <summary>Observes the number of aggregate statements and materialized Passage rows.</summary>
    public static readonly AsyncLocal<Action<int, int>?> ReadWork = new();
    private sealed record Cursor(string Project, string Source, string Generation, string Last);
    /// <summary>Reads a Source total and an optional bounded page of Passages.</summary>
    public static CommandOutcome<SourceProgressResponse> Query(string projectPath, string sourceId,
        string detail = "summary", int limit = 50, string? cursor = null)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || detail is not ("summary" or "passages") ||
            limit is < 1 or > 200 || detail == "summary" && cursor is not null)
            return Refuse(RefusalCodes.ProgressInvalidRequest, FailureReason.InvalidArgument, "Choose a Source and a valid detail and limit.");
        var source = sourceId.Trim().ToUpperInvariant();
        Cursor? continuation = null;
        if (cursor is not null)
        {
            try { continuation = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(cursor)); }
            catch (Exception e) when (e is FormatException or JsonException) { }
            if (continuation is null || continuation.Source != source || string.IsNullOrEmpty(continuation.Last))
                return Refuse(RefusalCodes.ProgressInvalidCursor, FailureReason.InvalidArgument, "Invalid Source Progress cursor.");
        }
        return ProjectStoreCommand.ReadExisting(projectPath,
            Refuse(RefusalCodes.SourceIndexMissing, FailureReason.NotFound, "The Source index is missing. Capture the saved project to queue it."),
            (connection, transaction, project) =>
            {
                var key = ProjectWorkspaceKey.Compute(project);
                if (continuation is not null && continuation.Project != key)
                    return Refuse(RefusalCodes.ProgressInvalidCursor, FailureReason.InvalidArgument, "The cursor belongs to another project.");
                var state = SourceIndexRepository.ReadState(connection, transaction, source);
                if (state is null)
                    return Refuse(RefusalCodes.SourceIndexMissing, FailureReason.NotFound, "The Source index is missing.");
                if (continuation is not null && continuation.Generation != state.Generation)
                    return Refuse(RefusalCodes.SourceGenerationChanged, FailureReason.Refused, "The Source generation changed.");
                var identity = SourceIndexIdentity.Parse(state.IdentityJson);
                var folder = ScriptureLocalStore.ForInstallation().ReadExisting(key).SourcePaths.GetValueOrDefault(state.SourceId);
                bool Current() => state.Status == "ready" && state.Kind == "Paratext" && identity is not null &&
                    identity.SourceStatus != "Removed" && identity.Error is null && folder is not null &&
                    Directory.Exists(folder) && File.Exists(Path.Combine(folder, "Settings.xml")) &&
                    state.TokenizerVersion == ParatextPassages.TokenizerVersion && state.AnchorVersion == ParatextPassages.AnchorVersion &&
                    identity.Current(project.FullFwDataPath, folder);
                if (!Current()) return Refuse(RefusalCodes.SourceIndexStale, FailureReason.Refused, "The Source index is stale or pending.");
                using var total = connection.CreateCommand();
                total.Transaction = transaction;
                total.CommandText = "SELECT COALESCE(SUM(EligiblePlaces),0) FROM SourcePassages WHERE SourceId=$source;";
                total.Parameters.AddWithValue("$source", source);
                var eligible = Convert.ToInt32(total.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
                var rows = new List<SourceProgressPassage>();
                if (detail == "passages")
                {
                    using var page = connection.CreateCommand();
                    page.Transaction = transaction;
                    page.CommandText = "SELECT PassageId,Locator,EligiblePlaces FROM SourcePassages WHERE SourceId=$source AND PassageId>$after ORDER BY PassageId LIMIT $limit;";
                    page.Parameters.AddWithValue("$source", source);
                    page.Parameters.AddWithValue("$after", continuation?.Last ?? "");
                    page.Parameters.AddWithValue("$limit", limit + 1);
                    using var reader = page.ExecuteReader();
                    while (reader.Read()) rows.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
                }
                ReadWork.Value?.Invoke(detail == "passages" ? 2 : 1, rows.Count);
                if (!Current()) return Refuse(RefusalCodes.SourceIndexStale, FailureReason.Refused, "The Source index changed during the read.");
                var next = rows.Count > limit ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new Cursor(key, source, state.Generation, rows[limit - 1].Id))) : null;
                return CommandOutcome<SourceProgressResponse>.Success(new(source, state.Generation, eligible, rows.Take(limit).ToArray(), next));
            });
    }
    private static CommandOutcome<SourceProgressResponse> Refuse(string code, FailureReason reason, string message) =>
        CommandOutcome<SourceProgressResponse>.Refused(new Refusal(code, reason, message));
}
