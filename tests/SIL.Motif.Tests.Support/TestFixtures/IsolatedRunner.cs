using SIL.Motif.Commands;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Launchers a test passes to a command explicitly, so the test process never sets a runner environment
/// variable.
/// </summary>
internal static class IsolatedRunner
{
    /// <summary>
    /// Starts the built worker under <paramref name="root"/>, in a namespace of its own so it never waits
    /// behind the developer's runner, and lets it exit a second after the queue is empty.
    /// </summary>
    public static ProcessRunnerLauncher Process(string root, string? parserPath = null) =>
        new(Options(root, parserPath));

    /// <summary>Queues work without starting any runner, for a test that must see the job stay queued.</summary>
    public static NoRunnerLauncher None(string root, string? parserPath = null) =>
        new(Options(root, parserPath));

    /// <summary>The settings <see cref="Process"/> launches with.</summary>
    public static JobRunnerLaunchOptions Options(string root, string? parserPath = null) =>
        new(root, parserPath ?? FakeParser.ExecutablePath)
        {
            WorkerExecutable = BuildOutput.Worker,
            OwnerNamespace = "motif-test-" + Guid.NewGuid().ToString("N"),
            IdleTimeout = TimeSpan.FromSeconds(1),
        };

    /// <summary>
    /// Waits until the runner process started for <paramref name="root"/> has exited, so a test deletes the
    /// parser it handed the runner only once nothing is running it. Call it only after the runner did work for
    /// the test: the runner publishes its identity file once it owns the root, so a root without one reads as
    /// "never started" and returns at once, even for a runner launched but not yet owning.
    /// </summary>
    public static void WaitUntilExited(string root) =>
        Wait.Until(() => !RunnerIsAlive(root, out _), "the runner for " + root + " did not exit",
            timeout: TimeSpan.FromMinutes(1), detail: () => Identity(root));

    /// <summary>
    /// The wait for a journey that has already failed: it gives the runner the same time to exit but never
    /// throws, so the journey's own failure is the one the test reports.
    /// </summary>
    public static void TryWaitUntilExited(string root) =>
        Wait.For(() => !RunnerIsAlive(root, out _), TimeSpan.FromMinutes(1));

    private static string Identity(string root)
    {
        var identity = RunnerIdentity.PathFor(root);
        RunnerIsAlive(root, out var pid);
        return "identity file " + identity + ", pid " + (pid?.ToString() ?? "unreadable");
    }

    // An unreadable or half-written identity file reads as alive: the runner is rewriting it, not gone.
    private static bool RunnerIsAlive(string root, out int? pid)
    {
        pid = null;
        var identity = RunnerIdentity.PathFor(root);
        if (!File.Exists(identity)) return false;
        string[] lines;
        try { lines = File.ReadAllLines(identity); }
        catch (IOException) { return true; }
        if (lines.Length == 0 || !int.TryParse(lines[0], out var id)) return true;
        pid = id;
        try
        {
            using var runner = System.Diagnostics.Process.GetProcessById(id);
            return !runner.HasExited;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
                                              or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
