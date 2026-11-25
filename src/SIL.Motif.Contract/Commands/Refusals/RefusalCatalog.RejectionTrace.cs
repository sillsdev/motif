using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsRejectionTrace() =>
    [
        Spec(RefusalCodes.RejectionTraceRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.RejectionTraceUnsupported, FailureReason.Refused),
        Spec(RefusalCodes.RejectionTraceCaptureRefused, FailureReason.Refused),
        Spec(RefusalCodes.RejectionTraceMalformed, FailureReason.Refused),
        Spec(RefusalCodes.RejectionTraceCaptureLimit, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesRejectionTrace() =>
    [
        RefusalCodes.RejectionTraceRequestInvalid,
        RefusalCodes.RejectionTraceUnsupported,
        RefusalCodes.RejectionTraceCaptureRefused,
        RefusalCodes.RejectionTraceMalformed,
        RefusalCodes.RejectionTraceCaptureLimit,
    ];
}
