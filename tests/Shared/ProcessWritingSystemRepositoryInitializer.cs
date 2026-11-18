using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
#pragma warning disable CA2255 // Test startup must complete ICU initialization before tests launch child processes.
    [ModuleInitializer]
    internal static void Install()
    {
        StaleTestDirectories.SweepTemporaryRoots();
        ProcessWritingSystemRepository.Install();
        // Configure locks before LibLCM starts so the host and its children share one repository lock.
        ConfigureWorkerLocks();
        // A developer who opts into online lookups must not make the suite, or the runners it starts, use the network.
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrOnlineVariable, null);
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

    private static void ConfigureWorkerLocks()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WorkerLockPaths.TestRegistryNamespaceVariable)))
            Environment.SetEnvironmentVariable(WorkerLockPaths.TestRegistryNamespaceVariable, ProcessIdentity.Name);
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WorkerLockPaths.TestLockNamespaceVariable)))
            Environment.SetEnvironmentVariable(WorkerLockPaths.TestLockNamespaceVariable,
                Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.WorkerLockRegistries", ProcessIdentity.Name));
    }
}
