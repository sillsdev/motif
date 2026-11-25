using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsTexts() =>
    [
        Spec(RefusalCodes.TextsWordsCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.TextsEvidenceChanged, FailureReason.Refused),
        Spec(RefusalCodes.TextsMixedEvidence, FailureReason.Refused),
        Spec(RefusalCodes.TextsOccurrenceNotFound, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesTexts() =>
    [
        RefusalCodes.TextsEvidenceChanged,
        RefusalCodes.TextsWordsCancelled,
    ];
}
