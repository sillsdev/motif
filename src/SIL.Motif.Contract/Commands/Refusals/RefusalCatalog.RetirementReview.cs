using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRetirementReview() =>
    [
        Spec(RefusalCodes.RetirementReviewDraftAmbiguous, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.RetirementReviewDraftNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.RetirementReviewEvidenceInconsistent, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.RetirementReviewInvalidDraft, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RetirementReviewStaleDryRun, FailureReason.Refused),
    ];
}
