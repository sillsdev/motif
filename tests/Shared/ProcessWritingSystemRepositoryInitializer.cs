using System.Runtime.CompilerServices;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
    [ModuleInitializer]
    internal static void Install()
    {
        // A test process killed mid-run abandons the machine-wide SLDR mutex; the next process would inherit it.
        SldrCacheMutex.ReclaimIfAbandoned();
        ProcessWritingSystemRepository.Install();
    }
}
