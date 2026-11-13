using System.Runtime.CompilerServices;
using SIL.Motif.Scripture;

namespace SIL.Motif.Tests.Scripture;

/// <summary>Points this test process at its own local Scripture file, so no run reads or changes the developer's real one.</summary>
internal static class ProcessScriptureLocalPathInitializer
{
#pragma warning disable CA2255 // The path must be set before any test creates a store.
    [ModuleInitializer]
    internal static void Install() =>
        Environment.SetEnvironmentVariable(ScriptureLocalStore.PathEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.ScriptureLocal", "process-" + Environment.ProcessId, "scripture-local.json"));
#pragma warning restore CA2255
}
