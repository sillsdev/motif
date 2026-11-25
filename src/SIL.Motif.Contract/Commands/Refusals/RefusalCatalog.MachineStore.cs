using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsMachineStore() =>
    [
        Spec(RefusalCodes.MachineStoreInconsistent, FailureReason.StoreInconsistent),
    ];
}
