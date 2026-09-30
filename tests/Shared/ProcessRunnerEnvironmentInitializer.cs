using System.Runtime.CompilerServices;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Gives this test process, and every CLI and runner process it starts, a runner owner lock and a worker root
/// of its own.
/// </summary>
/// <remarks>
/// <para>
/// Without a namespace a runner takes the one owner lock per user, which every test process on the machine
/// shares: a runner kicked for one test's root then waits for a runner serving another suite's root to go
/// idle, so suites in separate worktrees queue behind each other. A test about the shared lock sets its own
/// namespace, as <c>JobRunnerHostOwnershipTests</c> do.
/// </para>
/// <para>
/// Without a root, a command a test runs in-process registers projects and records usage in the per-user
/// machine database, the developer's real one: every suite on the machine then writes one SQLite file, and the
/// developer's own Motif lists test scratch projects as known projects. A test that names a root still wins.
/// </para>
/// </remarks>
internal static class ProcessRunnerEnvironmentInitializer
{
    /// <summary>The namespace this process sets, so a test proving no worker override is set can allow this one.</summary>
    internal static string Namespace { get; } = "motif-test-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");

    /// <summary>The worker root this process sets, under a root <see cref="StaleTestDirectories"/> sweeps.</summary>
    internal static string Root { get; } = Path.Combine(
        Path.GetTempPath(), StaleTestDirectories.WorkerRootsFolder, Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Install()
    {
        Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, Namespace);
        Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, Root);
    }
}
