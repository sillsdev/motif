using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Sources;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads published word places without loading a model, parsing Source text, or rebuilding an index.</summary>
public static class SourcePassageQuery
{
    private sealed record Cursor(string Project, string Source, string Passage, string Generation, int After);

    /// <summary>Reads one page from an existing, read-only project-store snapshot.</summary>
    public static CommandOutcome<SourcePassageResponse> Query(SourcePassageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) return SourceQuery.EmptyId<SourcePassageResponse>();
        var named = SourceProviders.Paratext.TryPassageId(request.Ref, out var passage) ||
            SourceProviders.FieldWorksTexts.TryPassageId(request.Ref, out passage);
        if (!named) return InvalidRef();
        if (request.Limit is < 1 or > 200)
            return Refused(RefusalCodes.SourcePassageLimitInvalid, FailureReason.InvalidArgument, "Choose a limit from 1 to 200.");
        var source = request.Source.Trim().ToUpperInvariant();
        Cursor? cursor = null;
        if (request.Cursor is not null && (!TryCursor(request.Cursor, out cursor) ||
            cursor!.Source != source || cursor.Passage != passage))
            return InvalidCursor();
        return ProjectStoreCommand.ReadExisting(request.ProjectPath,
            Refused(RefusalCodes.SourceIndexMissing, FailureReason.NotFound, "The Source index is missing. Capture the saved project to queue it."),
            (connection, transaction, project) =>
            {
                var key = ProjectWorkspaceKey.Compute(project);
                if (cursor is not null && cursor.Project != key) return InvalidCursor();
                var state = SourceIndexRepository.ReadState(connection, transaction, source);
                if (state is null)
                {
                    using var known = connection.CreateCommand();
                    known.Transaction = transaction;
                    known.CommandText = "SELECT 1 FROM SourceIndexState LIMIT 1;";
                    return known.ExecuteScalar() is null
                        ? Refused(RefusalCodes.SourceIndexMissing, FailureReason.NotFound,
                            "The Source index is missing. Capture the saved project to queue it.")
                        : SourceQuery.NotFound<SourcePassageResponse>(request.Source);
                }
                if (cursor is not null && cursor.Generation != state.Generation)
                    return Refused(RefusalCodes.SourceGenerationChanged, FailureReason.Refused,
                        "The Source generation changed. Start again at its first page.");
                if (state.Status == "pending") return Stale();
                var identity = SourceIndexIdentity.Parse(state.IdentityJson);
                if (identity is null || !identity.ProjectCurrent(project.FullFwDataPath)) return Stale();
                var provider = SourceProviders.For(state.Kind);
                if (provider is null)
                    return Refused(RefusalCodes.SourceKindUnsupported, FailureReason.InvalidArgument, "This kind of Source has no indexed Passages.");
                if (!provider.TryPassageId(request.Ref, out var named) || named != passage) return InvalidRef();
                if (identity.SourceStatus == "Removed")
                    return Refused(RefusalCodes.SourceIdRemoved, FailureReason.Refused, "This Source was removed.");
                var folder = provider.Folder(key, state.SourceId);
                if (provider.Unavailable(folder) is { } unavailable) return CommandOutcome<SourcePassageResponse>.Refused(unavailable);
                if (identity.Error is not null) return Refused(RefusalCodes.SourceIndexStale, FailureReason.Refused,
                    "The Source could not be indexed: " + identity.Error);
                if (state.Status != "ready" || state.TokenizerVersion != provider.TokenizerVersion ||
                    state.AnchorVersion != provider.AnchorVersion || !identity.Current(project.FullFwDataPath, folder))
                    return Stale();
                var stored = SourceIndexRepository.ReadPassage(connection, transaction, source, passage);
                if (stored is null) return Refused(RefusalCodes.SourceChapterNotInSource, FailureReason.InvalidArgument,
                    "The Source has no indexed Passage with this reference.");
                if (cursor is not null && cursor.After >= stored.TotalPlaces) return InvalidCursor();
                var page = SourceIndexRepository.ReadPage(connection, transaction, source, passage, cursor?.After ?? 0, request.Limit + 1);
                if (!identity.Current(project.FullFwDataPath, folder)) return Stale();
                var rows = page.Take(request.Limit).Select(row => new SourcePassagePlace(row.Place.Anchor, row.Place.Verse,
                    row.Place.Ordinal, row.Place.Surface, row.Wordform.WritingSystem, row.Place.WordformKey,
                    row.Wordform.FieldWorksId, row.Place.ReadingKey, row.Place.Signature, row.Place.State)).ToArray();
                var next = page.Count <= request.Limit ? null : Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new Cursor(key, source, passage, state.Generation, rows[^1].Ordinal)));
                return CommandOutcome<SourcePassageResponse>.Success(new(state.SourceId, state.Generation, stored.Locator,
                    stored.ContentHash, state.TokenizerVersion, state.AnchorVersion, rows, next));
            });
    }

    private static bool TryCursor(string value, out Cursor? cursor)
    {
        cursor = null;
        if (value.Length > 4096) return false;
        try
        {
            cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(value));
            return cursor is { Project.Length: > 0, Source.Length: > 0, Passage.Length: > 0, Generation.Length: > 0, After: > 0 };
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }

    private static CommandOutcome<SourcePassageResponse> InvalidRef() => Refused(RefusalCodes.SourceRefInvalid,
        FailureReason.InvalidArgument, "Name a Passage: a chapter such as MAT 5 for a Paratext Source, or a Text id for FieldWorks Texts.");
    private static CommandOutcome<SourcePassageResponse> Stale() => Refused(RefusalCodes.SourceIndexStale,
        FailureReason.Refused, "The Source index is stale or pending. Capture the saved project to queue a refresh.");
    private static CommandOutcome<SourcePassageResponse> InvalidCursor() => Refused(RefusalCodes.SourcePassageCursorInvalid,
        FailureReason.InvalidArgument, "The cursor is invalid or belongs to another project, Source or Passage.");
    private static CommandOutcome<SourcePassageResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<SourcePassageResponse>.Refused(new Refusal(code, reason, message));
}
