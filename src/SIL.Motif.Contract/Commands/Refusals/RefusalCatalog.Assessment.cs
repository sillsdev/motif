using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsAssessment() =>
    [
        Spec(RefusalCodes.AssessmentCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.AssessmentStoppedByBudget, FailureReason.StoppedByBudget),
        Spec(RefusalCodes.AssessmentAggregateUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.AssessmentBaselineUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.AssessmentExpired, FailureReason.NotFound),
        Spec(RefusalCodes.AssessmentInvalidEvidence, FailureReason.Refused),
        Spec(RefusalCodes.AssessmentInvalidId, FailureReason.InvalidArgument),
        Spec(RefusalCodes.AssessmentNotFound, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesAssessment() =>
    [
        RefusalCodes.AssessmentCancelled,
        RefusalCodes.AssessmentExpired,
        RefusalCodes.AssessmentStoppedByBudget,
    ];
}
