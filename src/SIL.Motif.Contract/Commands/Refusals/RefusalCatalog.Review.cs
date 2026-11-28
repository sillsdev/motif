using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsReview() =>
    [
        Spec(RefusalCodes.ReviewAssessmentNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.ReviewNumbersUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.ReviewWrongAssessmentKind, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ReviewDetailUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.ReviewInvalidMark, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ReviewUnreadable, FailureReason.StoreInconsistent),
    ];

    private static IEnumerable<string> WindowCodesReview() =>
    [
        RefusalCodes.ReviewAssessmentNotFound,
        RefusalCodes.ReviewNumbersUnavailable,
        RefusalCodes.ReviewWrongAssessmentKind,
    ];
}
