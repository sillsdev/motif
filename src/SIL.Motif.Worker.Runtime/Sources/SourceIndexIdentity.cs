using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Worker.Sources;

internal sealed record SourceFileStamp(string Name, long Length, long Ticks)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ReadError { get; init; }
}

internal sealed record SourceFolderInputs(string Folder, SourceFileStamp[] Files, int ReadError = 0)
{
    public bool Matches(SourceFolderInputs current) =>
        Folder == current.Folder && ReadError == current.ReadError && Files.SequenceEqual(current.Files);
}

internal sealed record SourceIndexIdentity(string Folder, string Context, string Versification, string SourceStatus,
    string WritingSystem, string ProjectIdentityJson, SourceFileStamp[] Files, string? Error = null,
    string[]? UnreadableApprovals = null, string[]? OrphanedApprovals = null,
    SIL.Motif.Contract.Responses.SourceApprovalConflict[]? ConflictedApprovals = null)
{
    internal static readonly AsyncLocal<Action<string>?> BeforeReadFile = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SourceFolderInputs? UnavailableInputs { get; init; }

    public static SourceFileStamp[] Metadata(string folder) => Directory.EnumerateFiles(folder)
        .OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path => ReadStamp(path, observeFailure: false)).ToArray();

    internal static SourceFolderInputs ObserveFolder(string? folder)
    {
        if (folder is null) return new("", []);
        folder = Path.GetFullPath(folder);
        try
        {
            var files = Directory.EnumerateFiles(folder).OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .Select(path => ReadStamp(path, observeFailure: true)).ToArray();
            return new(folder, files);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new(folder, [], exception.HResult);
        }
    }

    private static SourceFileStamp ReadStamp(string path, bool observeFailure)
    {
        SourceFileStamp? stamp = null;
        try
        {
            var info = new FileInfo(path);
            stamp = new SourceFileStamp(info.Name, info.Length, info.LastWriteTimeUtc.Ticks);
            BeforeReadFile.Value?.Invoke(path);
            using var readable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (observeFailure) _ = readable.ReadByte();
            return stamp;
        }
        catch (Exception exception) when (observeFailure && exception is IOException or UnauthorizedAccessException)
        {
            return (stamp ?? new SourceFileStamp(Path.GetFileName(path), 0, 0)) with { ReadError = exception.HResult };
        }
    }

    public bool ProjectCurrent(string projectPath) =>
        ProgressSourceIdentity.Parse(ProjectIdentityJson)?.Current(projectPath) == true;

    public bool Current(string projectPath, string? folder) => ProjectCurrent(projectPath) && FolderCurrent(folder);

    public bool FolderCurrent(string? folder) => (folder is null ? "" : Path.GetFullPath(folder)) == Folder &&
        Files.SequenceEqual(Directory.Exists(Folder) ? Metadata(Folder) : []);

    public string Serialize() => JsonSerializer.Serialize(this);
    public static SourceIndexIdentity? Parse(string json)
    {
        try
        {
            var identity = JsonSerializer.Deserialize<SourceIndexIdentity>(json);
            return identity is { Folder: not null, Context: not null, Versification: not null, SourceStatus: not null,
                WritingSystem.Length: > 0, ProjectIdentityJson: not null, Files: not null } &&
                identity.Files.All(file => file is { Name: not null, Length: >= 0 }) ? identity : null;
        }
        catch (JsonException) { return null; }
    }
}
