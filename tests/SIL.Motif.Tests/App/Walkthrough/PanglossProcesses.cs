using System.Diagnostics;
using System.ComponentModel;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>
/// Tracks parser processes started by this test after its snapshot and matching the configured executable.
/// Other Motif test hosts using that executable are indistinguishable, so a foreign parser that appears in
/// the window and survives can fail these tests.
/// </summary>
internal static class PanglossProcesses
{
    internal static HashSet<int> Snapshot()
    {
        var ids = new HashSet<int>();
        var executablePath = PanGlossExecutable.TryLocate();
        if (executablePath is null) return ids;

        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName("pangloss");
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
                        StringComparison.OrdinalIgnoreCase))
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

    internal static void TrackNew(IReadOnlySet<int> existing, ISet<int> appeared)
    {
        foreach (var id in Snapshot())
            if (!existing.Contains(id)) appeared.Add(id);
    }

    internal static bool AnyAlive(IEnumerable<int> ids) => Snapshot().Intersect(ids).Any();
}
