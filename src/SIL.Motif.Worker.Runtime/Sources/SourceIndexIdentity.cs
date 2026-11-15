using System.Text.Json;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Worker.Sources;

internal sealed record SourceFileStamp(string Name, long Length, long Ticks);

internal sealed record SourceIndexIdentity(string Folder, string Context, string Versification, string SourceStatus,
    string WritingSystem, string ProjectIdentityJson, SourceFileStamp[] Files, string? Error = null)
{
    public static SourceFileStamp[] Metadata(string folder) => Directory.EnumerateFiles(folder)
        .OrderBy(Path.GetFileName, StringComparer.Ordinal).Select(path =>
        {
            using var readable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var info = new FileInfo(path);
            return new SourceFileStamp(info.Name, info.Length, info.LastWriteTimeUtc.Ticks);
        }).ToArray();

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
