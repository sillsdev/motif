using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Host;

namespace SIL.Motif.Scripture;

/// <summary>What one project keeps on this machine only: the Current chapter of each Source and where each Source's folder is.</summary>
/// <param name="Current">One chapter per Source id.</param>
/// <param name="SourcePaths">The folder of each Source id on this machine.</param>
public sealed record ScriptureLocalState(
    IReadOnlyDictionary<string, ChapterRef> Current,
    IReadOnlyDictionary<string, string> SourcePaths)
{
    /// <summary>A project with nothing recorded.</summary>
    public static ScriptureLocalState Empty { get; } = new(
        new Dictionary<string, ChapterRef>(), new Dictionary<string, string>());
}

/// <summary>
/// The per-user file that holds <see cref="ScriptureLocalState"/> for every project: <c>scripture-local.json</c> in
/// Motif's per-user data folder (<c>&lt;user data&gt;/SIL/Motif</c>, the folder the runner root also uses).
/// </summary>
/// <remarks>
/// It lives outside every project folder and Baseline, so it is never synced and never copied with a project. The file is
/// read whole and written whole through a temporary file that is then moved over it, so a reader sees the old file or the
/// new one, never half of either. A file that is missing or unreadable reads as empty. Each change reads the file again
/// first, so two projects in different processes do not lose each other's entries (only two writes in the very same
/// instant can, and the later wins). The project id is any stable text the caller chooses for the project.
/// </remarks>
public sealed class ScriptureLocalStore
{
    /// <summary>The environment variable that names a different file; tests set it so they never touch the real one.</summary>
    public const string PathEnvironmentVariable = "MOTIF_SCRIPTURE_LOCAL_PATH";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, NewLine = "\n" };
    private static readonly object Gate = new();
    private readonly string _path;

    /// <summary>Uses the file at <paramref name="path"/>.</summary>
    public ScriptureLocalStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    /// <summary>The file for this user, or the one the test variable names.</summary>
    public static ScriptureLocalStore ForInstallation()
    {
        var configured = AmbientEnvironment.Get(PathEnvironmentVariable);
        return new ScriptureLocalStore(string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured);
    }

    /// <summary>The per-user file's normal place.</summary>
    public static string DefaultPath
    {
        get
        {
            var data = UserDataHome.Path;
            if (string.IsNullOrWhiteSpace(data)) throw new InvalidOperationException("The per-user data directory is unavailable.");
            return Path.Combine(data, "SIL", "Motif", "scripture-local.json");
        }
    }

    /// <summary>The file this store reads and writes.</summary>
    public string FilePath => _path;

    /// <summary>What is recorded for the project; <see cref="ScriptureLocalState.Empty"/> when nothing is.</summary>
    public ScriptureLocalState Read(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        lock (Gate) return ToState(Load().GetValueOrDefault(projectId));
    }

    /// <summary>
    /// Makes <paramref name="chapter"/> the Current chapter of the Source. When <paramref name="source"/> is given, a chapter
    /// the Paratext project does not hold is refused with <see cref="ArgumentException"/>. The book ID is normalized to upper case.
    /// </summary>
    public void SetCurrent(string projectId, string sourceId, ChapterRef chapter, ParatextSource? source = null)
    {
        var normalized = chapter with { Book = chapter.Book.ToUpperInvariant() };
        if (source is not null && !source.HasChapter(normalized))
            throw new ArgumentException($"The Source '{sourceId}' has no chapter {normalized}.", nameof(chapter));
        Change(projectId, sourceId, record => record.Current[sourceId] = normalized.ToString());
    }

    /// <summary>Forgets the Current chapter of the Source.</summary>
    public void ClearCurrent(string projectId, string sourceId) =>
        Change(projectId, sourceId, record => record.Current.Remove(sourceId));

    /// <summary>Records the folder of the Source on this machine.</summary>
    public void SetSourcePath(string projectId, string sourceId, string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Change(projectId, sourceId, record => record.SourcePaths[sourceId] = folder);
    }

    /// <summary>Forgets the folder of the Source.</summary>
    public void ClearSourcePath(string projectId, string sourceId) =>
        Change(projectId, sourceId, record => record.SourcePaths.Remove(sourceId));

    /// <summary>
    /// The folder to open for the Source: <paramref name="pluginPath"/> when the Paratext plugin supplied one (it always
    /// wins over the stored one), else the stored folder, else null.
    /// </summary>
    public string? ResolveSourcePath(string projectId, string sourceId, string? pluginPath = null)
    {
        if (!string.IsNullOrWhiteSpace(pluginPath)) return pluginPath;
        return Read(projectId).SourcePaths.GetValueOrDefault(sourceId);
    }

    private void Change(string projectId, string sourceId, Action<ProjectRecord> change)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        lock (Gate)
        {
            var all = Load();
            if (!all.TryGetValue(projectId, out var record)) all[projectId] = record = new ProjectRecord();
            change(record);
            if (record.Current.Count == 0 && record.SourcePaths.Count == 0) all.Remove(projectId);
            Save(all);
        }
    }

    private static ScriptureLocalState ToState(ProjectRecord? record)
    {
        if (record is null) return ScriptureLocalState.Empty;
        var current = new Dictionary<string, ChapterRef>();
        foreach (var (sourceId, text) in record.Current)
            if (TryParseChapter(text, out var chapter)) current[sourceId] = chapter;
        return new ScriptureLocalState(current, new Dictionary<string, string>(record.SourcePaths));
    }

    private static bool TryParseChapter(string text, out ChapterRef chapter)
    {
        chapter = default;
        var parts = text.Split(' ');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var number) || number < 1) return false;
        chapter = new ChapterRef(parts[0], number);
        return true;
    }

    private Dictionary<string, ProjectRecord> Load()
    {
        try
        {
            if (!File.Exists(_path)) return [];
            var document = JsonSerializer.Deserialize<FileDocument>(File.ReadAllText(_path), JsonOptions);
            return document?.Projects ?? [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return [];
        }
    }

    private void Save(Dictionary<string, ProjectRecord> projects)
    {
        var directory = Path.GetDirectoryName(_path) ?? throw new IOException("The local Scripture file has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".scripture-local-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, new FileDocument { Projects = projects }, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed class FileDocument
    {
        [JsonPropertyName("projects")]
        public Dictionary<string, ProjectRecord> Projects { get; set; } = [];
    }

    private sealed class ProjectRecord
    {
        [JsonPropertyName("current")]
        public Dictionary<string, string> Current { get; set; } = [];

        [JsonPropertyName("sourcePaths")]
        public Dictionary<string, string> SourcePaths { get; set; } = [];
    }
}
