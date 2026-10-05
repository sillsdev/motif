using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
#pragma warning disable CA2255 // Test startup must complete ICU initialization before tests launch child processes.
    [ModuleInitializer]
    internal static void Install()
    {
        StaleTestDirectories.SweepTemporaryRoots();
        ProcessWritingSystemRepository.Install();
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrOfflineVariable, "1");
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrCachePathVariable, ProcessWritingSystemRepository.SldrCachePath);
#if MOTIF_TESTS_SUPPORT
        ProcessMemoryDiagnostics.WriteStartupCheckpoint("test host before bundled ICU initialization");
#endif
        FwDataProjectLoader.Init();
#if MOTIF_TESTS_SUPPORT
        ProcessMemoryDiagnostics.WriteStartupCheckpoint("test host after bundled ICU initialization");
#endif
    }
#pragma warning restore CA2255
}
