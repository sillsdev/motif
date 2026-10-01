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
            processes = Process.GetProcesses();
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
                if (modulePath is not null && string.Equals(
                        Path.GetFullPath(modulePath), Path.GetFullPath(executablePath),
                        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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

    internal static string DescribeCandidates(string executablePath)
    {
        var candidates = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.ProcessName.Contains("pangloss", StringComparison.OrdinalIgnoreCase))
                        candidates.Add($"{process.Id}: {process.ProcessName} at {process.MainModule?.FileName}");
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    candidates.Add($"{process.Id}: {process.ProcessName}: {exception.Message}");
                }
            }
        }
        return $"Expected {Path.GetFullPath(executablePath)}; candidates: {string.Join("; ", candidates)}";
    }
}
