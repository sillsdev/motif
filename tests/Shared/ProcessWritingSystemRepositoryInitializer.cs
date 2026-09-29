using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
#pragma warning disable CA2255 // Test startup must complete ICU initialization before tests launch child processes.
    [ModuleInitializer]
    internal static void Install()
    {
        ProcessWritingSystemRepository.Install();
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrOfflineVariable, "1");
        FwDataProjectLoader.Init();
    }
#pragma warning restore CA2255
}
