using System.Runtime.CompilerServices;
using SIL.Motif.Scripture;

namespace SIL.Motif.Tests.Commands;

/// <summary>Points this test process at its own local Scripture file, so no run reads or changes the developer's real one.</summary>
internal static class ProcessScriptureLocalPathInitializer
{
    internal static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "SIL.Motif.Tests.ScriptureLocal.Commands", "process-" + Environment.ProcessId, "scripture-local.json");

#pragma warning disable CA2255 // The path must be set before any test creates a store.
    [ModuleInitializer]
    internal static void Install() =>
        Environment.SetEnvironmentVariable(ScriptureLocalStore.PathEnvironmentVariable, Path);
#pragma warning restore CA2255
}
