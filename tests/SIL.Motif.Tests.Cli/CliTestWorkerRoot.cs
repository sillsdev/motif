using System.IO;
using System.Runtime.CompilerServices;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.Cli;

internal static class CliTestWorkerRoot
{
    private static readonly string Root = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.Tests.Cli.Worker", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Install()
    {
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable(RunnerOptions.RootVariable, Root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
        };
    }
}
