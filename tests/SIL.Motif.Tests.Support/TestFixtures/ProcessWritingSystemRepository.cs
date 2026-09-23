using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Utils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Selects a writing-system repository for this test process and its child processes.
/// </summary>
/// <remarks>
/// <para>
/// The default machine-wide store is shared mutable state across processes. Without this configured path,
/// each <c>LcmCache.Dispose()</c> saves there, and each save first stashes the live LDML aside and then
/// moves it back. Two processes saving at once collide on that stash, so each save retries for about
/// 1.85 seconds and then fails silently. The suite runs as several concurrent test processes, so without
/// this every shard would slow its neighbours down. It would also leave in-flight stash files behind
/// that trip <see cref="PristineProjectFixture"/>'s stale-file guard. A private store also keeps test runs
/// from rewriting the developer's own shared writing systems.
/// </para>
/// <para>
/// The module initializer sets the path before any test, fixture or product code can open the first
/// cache. Child processes inherit it and install the same path when the host initializes, pinned by
/// <see cref="SIL.Motif.Tests.WritingSystems.ProcessWritingSystemRepositoryTests.LaunchedMotifProcessUsesTheSelectedRepositoryInsteadOfTheMachineWideRepository"/>.
/// </para>
/// </remarks>
internal static class ProcessWritingSystemRepository
{
    internal const string RepositoryPathVariable = "MOTIF_WRITING_SYSTEM_REPOSITORY_PATH";

    /// <summary>The directory this process's repository lives in.</summary>
    internal static string BasePath { get; } = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.Tests.WritingSystems", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

    /// <summary>The repository LibLCM will hand the next cache it opens.</summary>
    internal static object? Current => SingletonsContainer.Item(
        typeof(CoreGlobalWritingSystemRepository).FullName!);

    /// <summary>
    /// <see cref="Current"/> once no scratch open anywhere in the process holds its decoy in the slot.
    /// </summary>
    /// <remarks>
    /// <see cref="SIL.Motif.Host.LcmUtils.FwDataProjectLoader.LoadScratchCache"/> swaps a discarding decoy
    /// into the process-wide slot for the length of one load, and background work from another test may be
    /// inside that window at the moment of reading. The decoy is always swapped back out, so waiting it out
    /// observes the installed repository rather than a transient.
    /// </remarks>
    internal static object? CurrentOutsideScratchLoads()
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        var current = Current;
        while (current is SIL.Motif.Host.LcmUtils.DiscardingGlobalWritingSystemRepository && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
            current = Current;
        }
        return current;
    }

    static ProcessWritingSystemRepository()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(BasePath, recursive: true); }
            catch { /* best effort: a held handle at exit must not fail the run */ }
        };
    }

    internal static void Install()
    {
        Environment.SetEnvironmentVariable(RepositoryPathVariable, BasePath);
    }
}
