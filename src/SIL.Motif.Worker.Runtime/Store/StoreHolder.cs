using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Worker.Store;

/// <summary>A runner that has a project store open, as its sidecar file records it.</summary>
/// <param name="Pid">The runner's process id.</param>
/// <param name="Build">The build identity of the runner (see <see cref="RunnerIdentity.BuildId"/>).</param>
/// <param name="Root">The worker root the runner serves.</param>
/// <param name="StartedUtc">When the runner opened the store, as an ISO 8601 UTC time.</param>
public sealed record StoreHolderInfo(int Pid, string Build, string Root, string StartedUtc);

/// <summary>
/// Records which runner has a project store open, in a sidecar file beside it, so a refusal that finds the
/// store in use can name the process to stop.
/// </summary>
/// <remarks>
/// The use lease is shared, so two runners of different roots can hold one store; each writes its own file,
/// named by pid, and a reader keeps only the files whose process is still alive. A crashed runner's file is
/// deleted by the next reader. Pinned by `AHolderFileNamesTheLiveRunnerAndAFileForADeadProcessIsRemoved`.
/// </remarks>
public static class StoreHolder
{
    private const string Suffix = ".holder-";

    /// <summary>Where the process <paramref name="pid"/> records itself as a holder of <paramref name="storePath"/>.</summary>
    public static string PathFor(string storePath, int pid) =>
        storePath + Suffix + pid.ToString(CultureInfo.InvariantCulture);

    /// <summary>Records this process as a holder of <paramref name="storePath"/>; best-effort.</summary>
    /// <returns>A handle that removes the record when disposed.</returns>
    public static IDisposable Publish(string storePath, string root)
    {
        var path = PathFor(storePath, Environment.ProcessId);
        try
        {
            File.WriteAllText(path, string.Join("\n", Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                RunnerIdentity.BuildId, root, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        return new Mark(path);
    }

    /// <summary>The live holders of <paramref name="storePath"/>, oldest first; files of dead processes are removed.</summary>
    /// <param name="storePath">The project store whose holders to read.</param>
    /// <param name="isAlive">Decides whether a pid is a running process; the operating system's table when null.</param>
    public static IReadOnlyList<StoreHolderInfo> ReadLive(string storePath, Func<int, bool>? isAlive = null)
    {
        isAlive ??= IsAlive;
        var directory = Path.GetDirectoryName(storePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return [];
        var holders = new List<StoreHolderInfo>();
        string[] files;
        try { files = Directory.GetFiles(directory, Path.GetFileName(storePath) + Suffix + "*"); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return []; }
        foreach (var file in files)
        {
            var holder = Parse(file);
            if (holder is not null && isAlive(holder.Pid)) holders.Add(holder);
            else TryDelete(file);
        }
        return holders.OrderBy(holder => holder.StartedUtc, StringComparer.Ordinal).ToArray();
    }

    /// <summary>Removes every record that names <paramref name="pid"/> beside <paramref name="storePath"/>.</summary>
    public static void Remove(string storePath, int pid) => TryDelete(PathFor(storePath, pid));

    private static StoreHolderInfo? Parse(string file)
    {
        string[] lines;
        try { lines = File.ReadAllLines(file); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
        return lines.Length >= 4 &&
            int.TryParse(lines[0], NumberStyles.None, CultureInfo.InvariantCulture, out var pid)
            ? new StoreHolderInfo(pid, lines[1], lines[2], lines[3])
            : null;
    }

    internal static bool IsAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Mark : IDisposable
    {
        private string? _path;

        public Mark(string path) => _path = path;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _path, null) is { } path) TryDelete(path);
        }
    }
}
