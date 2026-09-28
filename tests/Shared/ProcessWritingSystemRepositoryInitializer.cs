using System.Runtime.CompilerServices;

namespace SIL.Motif.Tests.TestFixtures;

internal static class ProcessWritingSystemRepositoryInitializer
{
#pragma warning disable CA2255 // Support is a library, and its fixtures open a cache before test code runs.
    [ModuleInitializer]
    internal static void Install() => ProcessWritingSystemRepository.Install();
#pragma warning restore CA2255
}
