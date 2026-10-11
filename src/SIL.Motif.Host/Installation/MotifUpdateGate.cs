using System.Diagnostics;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Installation;

/// <summary>Coordinates package updates with work that uses the installed application files.</summary>
public static class MotifUpdateGate
{
    private const string GateName = "SIL.Motif.UpdateActivity";
    internal const string TestNamespaceVariable = "MOTIF_TEST_UPDATE_GATE_NAMESPACE";

    /// <summary>Acquires the activity lease, or returns <see langword="null"/> when work is active.</summary>
    public static IDisposable? TryAcquire()
    {
        return TryAcquire(GetGateName(), WorkerLockAccess.Shared);
    }

    /// <summary>Acquires exclusive access to apply a package update.</summary>
    /// <returns>A lease when no activity is using the installation; otherwise, <see langword="null"/>.</returns>
    public static IDisposable? TryAcquireForUpdate()
    {
        return TryAcquire(GetGateName(), WorkerLockAccess.Exclusive);
    }

    internal static IDisposable? TryAcquire(string gateName) =>
        TryAcquire(gateName, WorkerLockAccess.Shared);

    internal static IDisposable? TryAcquireForUpdate(string gateName) =>
        TryAcquire(gateName, WorkerLockAccess.Exclusive);

    private static IDisposable? TryAcquire(string gateName, WorkerLockAccess access)
    {
        var owner = new WorkerMutexOwner(gateName, machineWide: false, access);
        if (owner.TryAcquire())
            return owner;

        owner.Dispose();
        return null;
    }

    private static readonly TimeSpan RetirementRequestLifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan RetirementPoll = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether an updater has asked idle Workers to exit; a Worker with work in hand ignores it.</summary>
    public static bool IsWorkerRetirementRequested() => IsRetirementRequested(GetGateName());

    internal static bool IsRetirementRequested(string gateName)
    {
        var marker = GetRetirementMarkerPath(gateName);
        // A marker a crashed updater left behind goes stale rather than retiring every later Worker.
        return File.Exists(marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marker) < RetirementRequestLifetime;
    }

    /// <summary>
    /// Asks every idle Worker running from <paramref name="installDirectory"/> to exit, and waits for it.
    /// </summary>
    /// <remarks>
    /// An idle Worker holds no update lease, so the exclusive lease cannot see it, yet Velopack cannot swap the
    /// installed files under it (the update flow is ADR 0062). The request is a marker file; a Worker that is running a job leaves it
    /// unanswered (pinned by `ABusyRunnerIgnoresAnUpdatesRequestToExit`), and this refuses naming that process
    /// once the wait is over (pinned by `AWorkerThatDoesNotExitRefusesTheUpdateNamingItAndClearsTheRequest`).
    /// </remarks>
    /// <exception cref="InvalidOperationException">A Worker from the install is still running at the deadline.</exception>
    public static void RetireIdleWorkers(string installDirectory, TimeSpan timeout) =>
        RetireIdleWorkers(GetGateName(), installDirectory, timeout, FindInstalledWorkers);

    internal static void RetireIdleWorkers(string gateName, string installDirectory, TimeSpan timeout,
        Func<string, IReadOnlyList<WorkerProcessInfo>> findWorkers)
    {
        var marker = GetRetirementMarkerPath(gateName);
        File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        try
        {
            var deadline = DateTime.UtcNow + timeout;
            var remaining = findWorkers(installDirectory);
            while (remaining.Count > 0 && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(RetirementPoll);
                remaining = findWorkers(installDirectory);
            }
            if (remaining.Count == 0) return;
            throw new InvalidOperationException(
                "Motif cannot update while a Worker is still running from this installation: " +
                string.Join("; ", remaining.Select(worker => "PID " + worker.ProcessId + " " + worker.Path)) +
                ". It did not exit within " + timeout.TotalSeconds.ToString("0") + " seconds of being asked to.");
        }
        finally
        {
            try { File.Delete(marker); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal readonly record struct WorkerProcessInfo(int ProcessId, string Path);

    private static IReadOnlyList<WorkerProcessInfo> FindInstalledWorkers(string installDirectory) =>
        InstalledWorkerLocator.Find(installDirectory);

    private static string GetRetirementMarkerPath(string gateName) =>
        WorkerLockPaths.GetLockPath(gateName + ".RetireIdleWorkers", machineWide: false) + ".request";

    private static string GetGateName()
    {
        var testNamespace = Environment.GetEnvironmentVariable(TestNamespaceVariable);
        return string.IsNullOrWhiteSpace(testNamespace) ? GateName : GateName + ".Test." + testNamespace;
    }
}
