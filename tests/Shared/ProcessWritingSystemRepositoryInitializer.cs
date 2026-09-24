using System.Runtime.CompilerServices;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
    [ModuleInitializer]
    internal static void Install() => ProcessWritingSystemRepository.Install();
}
