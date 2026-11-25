using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsVariant() =>
    [
        Spec(RefusalCodes.VariantInvalidReference, FailureReason.Refused),
        Spec(RefusalCodes.VariantInvalidComponent, FailureReason.Refused),
        Spec(RefusalCodes.VariantInvalidType, FailureReason.Refused),
        Spec(RefusalCodes.VariantInvalidEntry, FailureReason.Refused),
        Spec(RefusalCodes.VariantAlreadyLinked, FailureReason.Refused),
        Spec(RefusalCodes.VariantSelfLink, FailureReason.Refused),
        Spec(RefusalCodes.VariantCycle, FailureReason.Refused),
        Spec(RefusalCodes.VariantStaleReference, FailureReason.Refused),
    ];
}
