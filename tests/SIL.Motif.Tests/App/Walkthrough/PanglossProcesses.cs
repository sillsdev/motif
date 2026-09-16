using System.Diagnostics;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class PanglossProcesses
{
    internal static HashSet<int> Snapshot()
    {
        var ids = new HashSet<int>();
        foreach (var process in Process.GetProcessesByName("pangloss"))
        {
            try { ids.Add(process.Id); }
            finally { process.Dispose(); }
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
