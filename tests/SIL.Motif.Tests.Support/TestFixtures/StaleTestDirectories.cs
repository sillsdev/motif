namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Deletes what earlier test processes left in the shared temporary roots this suite creates per process.
/// </summary>
/// <remarks>
/// A process that is killed, or whose cleanup meets a file still locked, leaves its directory behind, and
/// nothing else ever removes it: one developer machine had gathered 25 GB of them. A directory not written for
/// <see cref="StaleAfter"/> cannot belong to a live run, since a hung test fails well within that time. Pinned by
/// <c>SweepDeletesOnlyDirectoriesOlderThanTheCutoff</c>.
/// </remarks>
public static class StaleTestDirectories
{
    /// <summary>How long a directory must go unwritten before a sweep deletes it.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(6);

    /// <summary>The folder under the temporary directory that holds each test process's own worker root.</summary>
    public const string WorkerRootsFolder = "SIL.Motif.Tests.WorkerRoots";

    /// <summary>The per-process roots under the temporary directory that <see cref="SweepTemporaryRoots"/> clears.</summary>
    public static readonly string[] TemporaryRoots =
        ["SIL.Motif.Tests.Pristine", "SIL.Motif.Tests.Large", "SIL.Motif.Tests.WritingSystems", WorkerRootsFolder,
            Path.Combine("SIL.Motif.Walkthrough", "engine"), "SIL.Motif.WalkthroughDiffs"];

    /// <summary>Sweeps each of <see cref="TemporaryRoots"/>; never throws.</summary>
    public static void SweepTemporaryRoots()
    {
        foreach (var root in TemporaryRoots)
            Sweep(Path.Combine(Path.GetTempPath(), root), DateTime.UtcNow - StaleAfter);
    }

    /// <summary>
    /// Deletes every directory directly under <paramref name="root"/> last written before <paramref name="cutoffUtc"/>,
    /// skipping any that cannot be read or deleted.
    /// </summary>
    public static int Sweep(string root, DateTime cutoffUtc)
    {
        var deleted = 0;
        string[] entries;
        try { entries = Directory.Exists(root) ? Directory.GetDirectories(root) : []; }
        catch (IOException) { return 0; }
        catch (UnauthorizedAccessException) { return 0; }

        foreach (var entry in entries)
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(entry) >= cutoffUtc) continue;
                Directory.Delete(entry, recursive: true);
                deleted++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return deleted;
    }
}
