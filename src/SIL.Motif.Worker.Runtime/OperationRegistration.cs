using System.Runtime.CompilerServices;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Worker.Runtime;

internal static class OperationRegistration
{
#pragma warning disable CA2255 // The Worker parses stored Proposals, so the operation kinds must exist before any of its code runs.
    [ModuleInitializer]
    internal static void Register() => OperationRegistryBootstrap.Initialize();
#pragma warning restore CA2255
}
