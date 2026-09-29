using System.Runtime.CompilerServices;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessPanGlossQueueInitializer
{
    [ModuleInitializer]
    internal static void Install() => Environment.SetEnvironmentVariable(
        MachinePanGlossQueue.TestSlotNamespaceVariable,
        Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));
}
