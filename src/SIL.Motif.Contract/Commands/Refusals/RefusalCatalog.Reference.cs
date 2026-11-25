using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsReference() =>
    [
        Spec(RefusalCodes.ReferenceWrongList, FailureReason.Refused),
        Spec(RefusalCodes.ReferenceWrongClass, FailureReason.Refused),
        Spec(RefusalCodes.ReferenceUnknown, FailureReason.Refused),
    ];
}
