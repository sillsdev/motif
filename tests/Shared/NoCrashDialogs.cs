using System.Runtime.CompilerServices;
using SIL.Motif.Host;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Turns Windows' crash dialog off for the test process, so no process the suite launches can show one.
/// </summary>
/// <remarks>
/// The error mode is inherited, which covers a launched process that dies before its own startup code runs.
/// A crashing child is still reported through its exit code, which is what the suite asserts on.
/// </remarks>
internal static class NoCrashDialogs
{
    [ModuleInitializer]
    internal static void Install() => CrashDialogs.Suppress();
}
