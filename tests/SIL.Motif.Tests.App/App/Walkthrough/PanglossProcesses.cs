using System.Diagnostics;
using System.ComponentModel;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Tracks parser processes launched from a walkthrough's private executable copy.
/// </summary>
internal static class PanglossProcesses
{
    internal static string CopyExecutable(string managedRoot)
    {
        var source = PanGlossExecutable.TryLocate()
            ?? throw new InvalidOperationException(PanGlossExecutable.NotFoundMessage);
        var directory = Path.Combine(managedRoot, "pangloss-process-check", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, Path.GetFileName(source));
        File.Copy(source, executablePath);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(executablePath, File.GetUnixFileMode(source));
        return executablePath;
    }

    internal static HashSet<int> Snapshot(string executablePath)
    {
        var ids = new HashSet<int>();
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executablePath));
        }
        catch (Win32Exception)
        {
            return ids;
        }
        catch (InvalidOperationException)
        {
            return ids;
        }
        catch (UnauthorizedAccessException)
        {
            return ids;
        }

        foreach (var process in processes)
        {
            try
            {
                var modulePath = process.MainModule?.FileName;
                if (modulePath is not null && PathsMatch(modulePath, executablePath))
                    ids.Add(process.Id);
            }
            catch (Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
                // Another process's image path can name a directory that no longer resolves.
            }
            finally
            {
                process.Dispose();
            }
        }

        return ids;
    }

    internal static void TrackNew(string executablePath, IReadOnlySet<int> existing, ISet<int> appeared)
    {
        foreach (var id in Snapshot(executablePath))
            if (!existing.Contains(id)) appeared.Add(id);
    }

    internal static bool AnyAlive(string executablePath, IEnumerable<int> ids) =>
        Snapshot(executablePath).Intersect(ids).Any();

    internal static bool PathsMatch(string first, string second) =>
        string.Equals(CanonicalPath(first), CanonicalPath(second),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string CanonicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) return fullPath;
        var parent = Path.GetDirectoryName(fullPath)!;
        var root = Path.GetPathRoot(parent)!;
        var resolved = root;
        foreach (var segment in Path.GetRelativePath(root, parent).Split(Path.DirectorySeparatorChar))
        {
            resolved = Path.Combine(resolved, segment);
            resolved = new DirectoryInfo(resolved).ResolveLinkTarget(true)?.FullName ?? resolved;
        }
        return Path.Combine(resolved, Path.GetFileName(fullPath));
    }
}
