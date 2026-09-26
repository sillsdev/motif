using SIL.Motif.Commands;

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
}
