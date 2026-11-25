using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsAdhocProhibition() =>
    [
        Spec(RefusalCodes.AdhocProhibitionInvalidMorpheme, FailureReason.Refused),
        Spec(RefusalCodes.AdhocProhibitionInvalidAllomorph, FailureReason.Refused),
        Spec(RefusalCodes.AdhocProhibitionDuplicate, FailureReason.Refused),
        Spec(RefusalCodes.AdhocProhibitionSelf, FailureReason.Refused),
        Spec(RefusalCodes.AdhocProhibitionNoMorphologyData, FailureReason.Refused),
    ];
}
