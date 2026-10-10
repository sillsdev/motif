using System.Runtime.CompilerServices;

namespace SIL.Motif.Runner.Operations;

/// <summary>Initializes the Runner's operation-kind and handler registrations.</summary>
public static class OperationRegistryBootstrap
{
    /// <summary>
    /// Runs the Runner module initializers before a caller parses Proposal JSON through the Contract registry.
    /// </summary>
    public static void Initialize() =>
        RuntimeHelpers.RunModuleConstructor(typeof(LexicalSenseOperationKinds).Module.ModuleHandle);
}
