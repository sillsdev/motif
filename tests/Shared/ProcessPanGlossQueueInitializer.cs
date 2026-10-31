using System.Runtime.CompilerServices;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Installation;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessPanGlossQueueInitializer
{
    [ModuleInitializer]
    internal static void Install()
    {
        var processNamespace = ProcessIdentity.Name;
        Environment.SetEnvironmentVariable(MachinePanGlossQueue.TestSlotNamespaceVariable, processNamespace);
        Environment.SetEnvironmentVariable(MotifUpdateGate.TestNamespaceVariable, processNamespace);
        var lockRegistryNamespace = Environment.GetEnvironmentVariable(WorkerLockPaths.TestRegistryNamespaceVariable);
        if (string.IsNullOrWhiteSpace(lockRegistryNamespace)) lockRegistryNamespace = processNamespace;
        Environment.SetEnvironmentVariable(WorkerLockPaths.TestRegistryNamespaceVariable, lockRegistryNamespace);
        var lockNamespace = Environment.GetEnvironmentVariable(WorkerLockPaths.TestLockNamespaceVariable);
        if (string.IsNullOrWhiteSpace(lockNamespace))
            lockNamespace = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.WorkerLockRegistries", processNamespace);
        Environment.SetEnvironmentVariable(WorkerLockPaths.TestLockNamespaceVariable, lockNamespace);
    }
}
