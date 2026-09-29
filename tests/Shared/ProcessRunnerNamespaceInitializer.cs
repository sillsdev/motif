using System.Runtime.CompilerServices;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Gives this test process, and every CLI and runner process it starts, a runner owner lock of its own.
/// </summary>
/// <remarks>
/// Without a namespace a runner takes the one owner lock per user, which every test process on the machine
/// shares: a runner kicked for one test's root then waits for a runner serving another suite's root to go
/// idle, so suites in separate worktrees queue behind each other. A test about the shared lock sets its own
/// namespace, as <c>JobRunnerHostOwnershipTests</c> do.
/// </remarks>
internal static class ProcessRunnerNamespaceInitializer
{
    /// <summary>The namespace this process sets, so a test proving no worker override is set can allow this one.</summary>
    internal static string Namespace { get; } = "motif-test-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N");

    [ModuleInitializer]
    internal static void Install() => Environment.SetEnvironmentVariable(RunnerOptions.NamespaceVariable, Namespace);
}
