using System.Runtime.CompilerServices;
using SIL.Motif.Scripture;

namespace SIL.Motif.Tests.App;

/// <summary>Isolates Source settings for this App test process.</summary>
internal static class ProcessScriptureLocalPathInitializer
{
    internal static readonly string Path = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "SIL.Motif.Tests.ScriptureLocal.App",
        "process-" + Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"), "scripture-local.json");

#pragma warning disable CA2255 // The path must be set before tests construct a store.
    [ModuleInitializer]
    internal static void Install() =>
        Environment.SetEnvironmentVariable(ScriptureLocalStore.PathEnvironmentVariable, Path);
#pragma warning restore CA2255
}
