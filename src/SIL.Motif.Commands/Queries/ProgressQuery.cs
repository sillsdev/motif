using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Worker.Baselines;
using WorkerBuckets = SIL.Motif.Worker.Baselines.ProgressBuckets;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads Text Progress from its saved, revisioned aggregate without opening the project model.</summary>
public static class ProgressQuery
{
    private sealed record Cursor(string Revision, string ScopeDigest, string LastText, string? SinceRevision);
    private sealed record ScopeAggregate(SIL.Motif.Worker.Baselines.ProgressCounts Counts, IReadOnlyDictionary<string, int> Excluded, int Passages);
    private static readonly object ScopeLock = new();
    private static readonly Dictionary<(string Project, string Revision, string Digest), ScopeAggregate> ScopeCache = new();

    /// <summary>Reads one summary or a revision-bound page of Texts.</summary>
    public static CommandOutcome<ProgressResponse> Query(ProgressRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Scope is null || request.Scope.Kind != "texts" || request.Scope.Source is not null || request.Scope.Texts?.Count > 200 ||
            request.Detail is not ("summary" or "passages") || request.Limit is < 1 or > 200 ||
            request.Detail == "summary" && request.Cursor is not null)
            return Refused(RefusalCodes.ProgressInvalidRequest, FailureReason.InvalidArgument,
                "Choose Texts and summary or passages, with a limit from 1 to 200.");
        var ids = request.Scope.Texts?.Distinct().OrderBy(id => id.ToString("D"), StringComparer.Ordinal).ToArray() ?? [];
        if (ids.Any(id => id == Guid.Empty))
            return Refused(RefusalCodes.ProgressInvalidRequest, FailureReason.InvalidArgument, "Text GUIDs must be nonempty.");
        var digest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(
            Encoding.UTF8.GetBytes("texts:" + string.Join(',', ids.Select(id => id.ToString("D"))))));
        Cursor? cursor = null;
        if (request.Cursor is not null && (!TryCursor(request.Cursor, out cursor) || cursor!.ScopeDigest != digest ||
            cursor.SinceRevision != request.SinceRevision))
            return Refused(RefusalCodes.ProgressInvalidCursor, FailureReason.InvalidArgument,
                "The cursor is invalid or belongs to another Text scope.");

        var absent = Refused(RefusalCodes.ProgressIndexNotReady, FailureReason.Refused,
            "Progress is not ready. Refresh the saved project to rebuild it.");
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, absent, (connection, transaction, project) =>
        {
            var state = ProgressRepository.ReadState(connection, transaction);
            if (state is null || state.PendingReceiptId is not null)
                return Refused(RefusalCodes.ProgressIndexNotReady, FailureReason.Refused,
                    "Progress is not ready. Refresh the saved project to rebuild it.");
            if (cursor is not null && cursor.Revision != state.Revision)
                return Refused(RefusalCodes.ProgressRevisionChanged, FailureReason.Refused,
                    "Progress changed since this page was requested. Reload the summary.");

            IReadOnlySet<Guid>? selected = ids.Length == 0 ? null : ids.ToHashSet();
            ScopeAggregate aggregate;
            if (selected is null)
            {
                using var count = connection.CreateCommand();
                count.Transaction = transaction;
                count.CommandText = "SELECT COUNT(*) FROM ProgressTexts;";
                aggregate = new ScopeAggregate(state.Totals, state.Excluded,
                    Convert.ToInt32(count.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                var key = (project.FullFwDataPath, state.Revision, digest);
                lock (ScopeLock) ScopeCache.TryGetValue(key, out aggregate!);
                if (aggregate is null)
                {
                    var scopeRows = ProgressRepository.ReadTexts(connection, transaction, selected, null, null, true);
                    if (scopeRows.Count != selected.Count)
                        return Refused(RefusalCodes.ProgressTextNotFound, FailureReason.NotFound,
                            "One or more requested Texts are absent from the saved Progress projection.");
                    aggregate = new ScopeAggregate(ProgressProjectionBuilder.Aggregate(scopeRows.Select(row => row.Text)),
                        Excluded(scopeRows.Select(row => row.Text)), scopeRows.Count);
                    lock (ScopeLock)
                    {
                        if (ScopeCache.Count >= 128) ScopeCache.Clear();
                        ScopeCache[key] = aggregate;
                    }
                }
            }
            var counts = aggregate.Counts;
            var excluded = aggregate.Excluded;
            var passages = aggregate.Passages;
            var page = request.Detail == "passages"
                ? ProgressRepository.ReadTexts(connection, transaction, selected, cursor?.LastText, request.Limit + 1, false)
                : [];
            var next = page.Count > request.Limit
                ? EncodeCursor(new Cursor(state.Revision, digest, page[request.Limit - 1].Text.Id.ToString("D"),
                    request.SinceRevision)) : null;
            var changed = new ProgressChangedSince(request.SinceRevision);
            var rows = page.Take(request.Limit).Select(row => new ProgressPassage(row.Text.Id, row.Text.Title,
                ToWire(row.Counts.Tokens), ToWire(row.Counts.Types), changed)).ToArray();
            // Identical lengths and write times can conceal byte changes until Apply or refresh hashes the inputs.
            var current = ProgressSourceIdentity.Parse(state.SourceIdentityJson)?.Current(project.FullFwDataPath) == true;
            var limitations = new List<string> { "checked-unavailable", "changed-since-unavailable", "parser-evidence-unavailable" };
            if (!current) limitations.Add("judgements-stale");
            var coverage = new ProgressCoverage("fieldworks-segments-v1", "ws-valid-characters-v1",
                "least-complete-place-v1", counts.Tokens.Unmeasured == 0, excluded.Values.Sum(), excluded);
            return CommandOutcome<ProgressResponse>.Success(new ProgressResponse(state.Revision, digest, coverage,
                new ProgressFreshness(state.ContentRevision, state.JudgementRevision, current),
                request.Detail == "summary" ? new SIL.Motif.Contract.Responses.ProgressCounts(passages,
                    ToWire(counts.Tokens), ToWire(counts.Types)) : null,
                rows, next, limitations, changed));
        });
    }

    private static SIL.Motif.Contract.Responses.ProgressBuckets ToWire(WorkerBuckets value) =>
        new(value.Total, value.Approved, value.Misspelled, value.Unmeasured, value.Reasons);

    private static IReadOnlyDictionary<string, int> Excluded(IEnumerable<ProgressText> texts)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in texts.SelectMany(text => text.Excluded))
            result[pair.Key] = result.GetValueOrDefault(pair.Key) + pair.Value;
        return result;
    }

    private static string EncodeCursor(Cursor cursor) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryCursor(string value, out Cursor? cursor)
    {
        cursor = null;
        if (value.Length is 0 or > 2048) return false;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight((encoded.Length + 3) / 4 * 4, '=');
            cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(encoded));
            return cursor is not null && !string.IsNullOrEmpty(cursor.Revision) &&
                Guid.TryParseExact(cursor.LastText, "D", out _) && !string.IsNullOrEmpty(cursor.ScopeDigest);
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }

    private static CommandOutcome<ProgressResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<ProgressResponse>.Refused(new Refusal(code, reason, message));
}
