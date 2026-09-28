using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessSldrOfflineInitializer
{
    // Before any cache opens, so neither this process nor a runner it starts queues on the SLDR's network lock.
#pragma warning disable CA2255 // Support is a library, and its fixtures open a cache before test code runs.
    [ModuleInitializer]
    internal static void Install() =>
        Environment.SetEnvironmentVariable(FwDataProjectLoader.SldrOfflineVariable, "1");
#pragma warning restore CA2255
}
