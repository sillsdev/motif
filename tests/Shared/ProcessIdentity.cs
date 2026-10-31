namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Names this test process by its id and start time, so every test assembly in it computes the same value.
/// </summary>
/// <remarks>
/// Each test assembly compiles its own copy of this class and of the module initializers that use it, and a test
/// that loads another test assembly by path (<c>TestLevelGuardTests</c>) runs that copy's initializers. With a
/// random part per copy, those initializers replaced the host's namespaces and roots mid-run, and a test then read
/// an environment value that was not the one its own assembly's statics held. The start time keeps the name unique
/// when a process id is reused.
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
