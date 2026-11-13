using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Sets the folder and the Current chapter of a Source on this machine. Both live only in the local file
/// (<c>scripture-local.json</c>, per user, never synced): no Draft, no Apply, and FieldWorks may be open. The Source must
/// exist in the project's applied Source list; the local file is keyed by <see cref="ProjectWorkspaceKey.Compute"/>.
/// </summary>
public static class SourceLocalCommand
{
    /// <summary>Records the folder of a Source; the folder must exist.</summary>
    public static CommandOutcome<SourceLocalResponse> SetFolder(SourceSetFolderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) return SourceQuery.EmptyId<SourceLocalResponse>();
        if (string.IsNullOrWhiteSpace(request.Path) || !Directory.Exists(request.Path))
            return FolderMissing(request.Path);
        return Change(request.ProjectPath, request.Source, (store, key, entry) =>
        {
            var folder = Path.GetFullPath(request.Path);
            store.SetSourcePath(key, entry.Id, folder);
            return new SourceLocalResponse(entry.Id, folder, null);
        });
    }

    /// <summary>Forgets the folder of a Source; none recorded is fine.</summary>
    public static CommandOutcome<SourceLocalResponse> ClearFolder(SourceClearFolderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) return SourceQuery.EmptyId<SourceLocalResponse>();
        return Change(request.ProjectPath, request.Source, (store, key, entry) =>
        {
            store.ClearSourcePath(key, entry.Id);
            return new SourceLocalResponse(entry.Id, null, null);
        });
    }

    /// <summary>
    /// Makes a chapter the Current chapter. With a folder recorded for the Source the chapter is checked against the opened
    /// Paratext text; with none recorded it is taken as given.
    /// </summary>
    public static CommandOutcome<SourceLocalResponse> SetCurrent(CurrentSetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) return SourceQuery.EmptyId<SourceLocalResponse>();
        if (!SourceQuery.TryParseChapter(request.Ref, out var chapter))
            return CommandOutcome<SourceLocalResponse>.Refused(new Refusal(RefusalCodes.SourceRefInvalid, FailureReason.InvalidArgument,
                $"'{request.Ref}' is not a chapter. Write a book and a chapter number, for example MAT 5."));
        return Change(request.ProjectPath, request.Source, (store, key, entry) =>
        {
            var folder = store.Read(key).SourcePaths.GetValueOrDefault(entry.Id);
            ParatextSource? text = null;
            if (folder is not null)
            {
                if (!Directory.Exists(folder)) return FolderMissing(folder);
                try { text = ParatextSourceReader.Open(folder); }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
                {
                    return CommandOutcome<SourceLocalResponse>.Refused(new Refusal(RefusalCodes.SourceFolderMissing, FailureReason.NotFound,
                        $"The folder of '{entry.Id}' could not be read: {exception.Message}",
                        new Dictionary<string, string> { ["path"] = folder }));
                }
            }
            if (text is not null && !text.HasChapter(chapter))
                return CommandOutcome<SourceLocalResponse>.Refused(new Refusal(RefusalCodes.SourceChapterNotInSource, FailureReason.InvalidArgument,
                    $"The Source '{entry.Id}' has no chapter {chapter}.",
                    new Dictionary<string, string> { ["source"] = entry.Id, ["chapter"] = chapter.ToString() }));
            store.SetCurrent(key, entry.Id, chapter);
            return CommandOutcome<SourceLocalResponse>.Success(new SourceLocalResponse(entry.Id, folder, chapter.ToString()));
        });
    }

    /// <summary>Forgets the Current chapter of a Source; none recorded is fine.</summary>
    public static CommandOutcome<SourceLocalResponse> ClearCurrent(CurrentClearRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Source)) return SourceQuery.EmptyId<SourceLocalResponse>();
        return Change(request.ProjectPath, request.Source, (store, key, entry) =>
        {
            store.ClearCurrent(key, entry.Id);
            return new SourceLocalResponse(entry.Id, store.Read(key).SourcePaths.GetValueOrDefault(entry.Id), null);
        });
    }

    private static CommandOutcome<SourceLocalResponse> Change(string projectPath, string source,
        Func<ScriptureLocalStore, string, SIL.Motif.Scripture.SourceListEntry, SourceLocalResponse> change) =>
        Change(projectPath, source, (store, key, entry) => CommandOutcome<SourceLocalResponse>.Success(change(store, key, entry)));

    private static CommandOutcome<SourceLocalResponse> Change(string projectPath, string source,
        Func<ScriptureLocalStore, string, SIL.Motif.Scripture.SourceListEntry, CommandOutcome<SourceLocalResponse>> change) =>
        SourceQuery.Read<SourceLocalResponse>(projectPath, null, (context, _, _) =>
        {
            var entry = context.Find(source);
            if (entry is null) return SourceQuery.NotFound<SourceLocalResponse>(source);
            return change(ScriptureLocalStore.ForInstallation(), context.ProjectId, entry);
        });

    private static CommandOutcome<SourceLocalResponse> FolderMissing(string? path) => CommandOutcome<SourceLocalResponse>.Refused(new Refusal(
        RefusalCodes.SourceFolderMissing, FailureReason.NotFound, $"The folder '{path}' does not exist on this machine.",
        new Dictionary<string, string> { ["path"] = path ?? "" }));
}
