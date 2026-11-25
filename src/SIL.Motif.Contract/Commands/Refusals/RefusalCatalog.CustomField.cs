using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsCustomField() =>
    [
        Spec(RefusalCodes.CustomFieldMissing, FailureReason.Refused),
        Spec(RefusalCodes.CustomFieldNotCustom, FailureReason.Refused),
        Spec(RefusalCodes.CustomFieldWrongType, FailureReason.Refused),
        Spec(RefusalCodes.CustomFieldWrongClass, FailureReason.Refused),
        Spec(RefusalCodes.CustomFieldWrongList, FailureReason.Refused),
        Spec(RefusalCodes.CustomFieldWritingSystem, FailureReason.Refused),
    ];
}
