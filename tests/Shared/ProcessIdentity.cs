namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Names the test process by its id and start time for every test assembly loaded in it.
/// </summary>
/// <remarks>
/// Loading another test assembly can run its module initializers, as in <c>TestLevelGuardTests</c>. The shared
/// name keeps those initializers from changing the host's namespaces and roots. The start time keeps the name
/// unique when a process id is reused.
/// </remarks>
internal static class ProcessIdentity
{
    internal static string Name { get; } = Create();

    private static string Create()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return Environment.ProcessId + "-" + process.StartTime.Ticks.ToString("x");
    }
}
