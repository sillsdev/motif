using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsHandoff() =>
    [
        Spec(RefusalCodes.HandoffCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.HandoffDestinationExists, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HandoffInvocationMismatch, FailureReason.Refused),
        Spec(RefusalCodes.HandoffInvocationNotFound, FailureReason.NotFound),
        Spec(RefusalCodes.HandoffInvocationRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HandoffParserUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandoffSourceUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandoffStatisticsUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandoffTextNotFound, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HandoffTraceBaselineUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandoffTraceUnavailable, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesHandoff() =>
    [
        RefusalCodes.HandoffCancelled,
        RefusalCodes.HandoffDestinationExists,
        RefusalCodes.HandoffInvocationMismatch,
        RefusalCodes.HandoffInvocationNotFound,
        RefusalCodes.HandoffInvocationRequired,
        RefusalCodes.HandoffParserUnavailable,
        RefusalCodes.HandoffSourceUnavailable,
        RefusalCodes.HandoffStatisticsUnavailable,
        RefusalCodes.HandoffTextNotFound,
    ];
}
