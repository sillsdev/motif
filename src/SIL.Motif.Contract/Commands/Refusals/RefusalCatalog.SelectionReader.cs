using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsSelectionReader() =>
    [
        Spec(RefusalCodes.SelectionReaderCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.SelectionReaderDisposed, FailureReason.Refused),
        Spec(RefusalCodes.SelectionReaderBusy, FailureReason.Busy),
        Spec(RefusalCodes.SelectionReaderInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionReaderNoBaseline, FailureReason.Refused),
        Spec(RefusalCodes.SelectionReaderRangeInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionReaderOwnershipLimit, FailureReason.Busy),
        Spec(RefusalCodes.SelectionReaderProjectPathRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.SelectionReaderOpenFailed, FailureReason.Refused),
        Spec(RefusalCodes.SelectionReaderPresentationChanged, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesSelectionReader() =>
    [
        RefusalCodes.SelectionReaderBusy,
        RefusalCodes.SelectionReaderCancelled,
        RefusalCodes.SelectionReaderDisposed,
        RefusalCodes.SelectionReaderInvalid,
        RefusalCodes.SelectionReaderNoBaseline,
        RefusalCodes.SelectionReaderOwnershipLimit,
        RefusalCodes.SelectionReaderProjectPathRequired,
        RefusalCodes.SelectionReaderRangeInvalid,
    ];
}
