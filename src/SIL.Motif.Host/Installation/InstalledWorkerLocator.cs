using System.Diagnostics;
using System.Text;

namespace SIL.Motif.Host.Installation;

/// <summary>Finds running Workers by the executable file they were started from, not by process name.</summary>
/// <remarks>
/// A process name is the kernel's 15-character comm on Linux, taken from argv[0] and not from the file, so a
/// Worker started through a symlink or a renamed link has a name that says nothing about its install
/// (pinned by `AWorkerStartedThroughASymlinkIsFoundByItsExecutablePath`). The executable path is the identity.
/// An AppImage mounts afresh on every launch, so the App and the CLI of one installed image can run from
/// different mount directories; a Worker also counts when it carries the same <c>APPIMAGE</c> file as the
/// updater, which is the thing an update replaces (pinned by `AWorkerOfTheSameAppImageIsFoundFromAnotherMount`).
/// </remarks>
internal static class InstalledWorkerLocator
{
    internal const string AppImageVariable = "APPIMAGE";

    private const string WorkerFileName = "SIL.Motif.Worker";

    // Cheap pre-filter for the platforms that list processes by name; macOS and Windows names run longer than this.
    private const string NamePrefix = "SIL.Motif.Wor";

    internal static IReadOnlyList<MotifUpdateGate.WorkerProcessInfo> Find(string installDirectory)
    {
        var appImage = OperatingSystem.IsLinux() ? Environment.GetEnvironmentVariable(AppImageVariable) : null;
        return OperatingSystem.IsLinux()
            ? FindOnLinux("/proc", installDirectory, appImage)
            : FindWithProcessApi(installDirectory);
    }

    internal static bool IsWorkerUnder(string installDirectory, string executablePath)
    {
        var root = Path.GetFullPath(installDirectory).TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = PathComparison;
        var full = Path.GetFullPath(executablePath);
        return full.StartsWith(root, comparison) && IsWorkerFile(full);
    }

    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static bool IsWorkerFile(string path)
    {
        var name = Path.GetFileName(path);
        return string.Equals(name, WorkerFileName, PathComparison) ||
            (OperatingSystem.IsWindows() && string.Equals(name, WorkerFileName + ".exe", PathComparison));
    }

    internal static IReadOnlyList<MotifUpdateGate.WorkerProcessInfo> FindOnLinux(
        string procRoot, string installDirectory, string? appImagePath)
    {
        var found = new List<MotifUpdateGate.WorkerProcessInfo>();
        foreach (var directory in Directory.EnumerateDirectories(procRoot))
        {
            if (!int.TryParse(Path.GetFileName(directory), out var pid)) continue;
            var path = ReadExecutable(directory);
            if (path is null || !IsWorkerFile(path)) continue;
            if (IsWorkerUnder(installDirectory, path) || RunsFromAppImage(directory, appImagePath))
                found.Add(new MotifUpdateGate.WorkerProcessInfo(pid, path));
        }
        return found;
    }

    private static string? ReadExecutable(string procDirectory)
    {
        try
        {
            // A process that has gone, or one of another user, has no readable link; neither can be ours to retire.
            var target = new FileInfo(Path.Combine(procDirectory, "exe")).LinkTarget;
            const string deleted = " (deleted)";
            return target is not null && target.EndsWith(deleted, StringComparison.Ordinal)
                ? target[..^deleted.Length]
                : target;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool RunsFromAppImage(string procDirectory, string? appImagePath)
    {
        if (string.IsNullOrWhiteSpace(appImagePath)) return false;
        byte[] environment;
        try { environment = File.ReadAllBytes(Path.Combine(procDirectory, "environ")); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        var wanted = Path.GetFullPath(appImagePath);
        var prefix = AppImageVariable + "=";
        foreach (var entry in Encoding.UTF8.GetString(environment).Split('\0'))
            if (entry.StartsWith(prefix, StringComparison.Ordinal) &&
                string.Equals(Path.GetFullPath(entry[prefix.Length..]), wanted, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static IReadOnlyList<MotifUpdateGate.WorkerProcessInfo> FindWithProcessApi(string installDirectory)
    {
        var found = new List<MotifUpdateGate.WorkerProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                string? path;
                try
                {
                    if (!process.ProcessName.StartsWith(NamePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    path = process.MainModule?.FileName;
                }
                catch (Exception exception) when (exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or NotSupportedException)
                {
                    continue;
                }
                if (path is not null && IsWorkerUnder(installDirectory, path))
                    found.Add(new MotifUpdateGate.WorkerProcessInfo(process.Id, path));
            }
        }
        return found;
    }
}
