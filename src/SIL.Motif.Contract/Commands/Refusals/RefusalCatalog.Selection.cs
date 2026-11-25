using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsSelection() =>
    [
        Spec(RefusalCodes.SelectionDefaultMissing, FailureReason.Refused),
        Spec(RefusalCodes.SelectionEmpty, FailureReason.Refused),
        Spec(RefusalCodes.SelectionInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.SelectionParsimonyNameIsDefault, FailureReason.Refused),
        Spec(RefusalCodes.SelectionTextNotFound, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionInvalidLimits, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceMismatch, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceNotFound, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceProjectMismatch, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetrySourceWithoutRetry, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRetryWorkInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionRevisionConflict, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesSelection() =>
    [
        RefusalCodes.SelectionDefaultMissing,
        RefusalCodes.SelectionEmpty,
        RefusalCodes.SelectionInvalid,
        RefusalCodes.SelectionTextNotFound,
    ];
}
