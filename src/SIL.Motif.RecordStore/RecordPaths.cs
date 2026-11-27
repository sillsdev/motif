namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>Resolves filesystem aliases so a database, its writer lease and its resources share one physical path.</summary>
    public static string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? throw new ArgumentException("Invalid store path.", nameof(path));
        var current = root;
        var remaining = new LinkedList<string>(Segments(full[root.Length..]));
        var hops = 0;
        while (remaining.First is { } first)
        {
            remaining.RemoveFirst();
            var candidate = Path.Combine(current, first.Value);
            FileSystemInfo entry = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            if (entry.LinkTarget is null) { current = candidate; continue; }
            var target = entry.ResolveLinkTarget(returnFinalTarget: false) ?? throw new IOException("Unresolved store alias.");
            if (++hops > 40) throw new IOException("The store path passes through too many symbolic links.");
            // Restart each target because its parents may themselves be aliases, including /var on macOS.
            var next = Path.GetFullPath(target.FullName);
            var nextRoot = Path.GetPathRoot(next) ?? throw new IOException("Invalid store alias target.");
            current = nextRoot;
            var segments = Segments(next[nextRoot.Length..]);
            for (var index = segments.Length - 1; index >= 0; index--) remaining.AddFirst(segments[index]);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private static string[] Segments(string path) => path.Split(
        [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
}
