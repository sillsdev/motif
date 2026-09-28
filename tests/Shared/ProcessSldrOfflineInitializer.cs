using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessSldrOfflineInitializer
{
    // Before any cache opens, so neither this process nor a runner it starts queues on the SLDR's network lock.
    [ModuleInitializer]
    internal static void Install() =>
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrOfflineVariable, "1");
}
