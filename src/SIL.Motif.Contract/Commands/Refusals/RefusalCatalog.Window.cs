using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsWindow() =>
    [
        Spec(RefusalCodes.WindowCheck, FailureReason.Refused),
        Spec(RefusalCodes.WindowDiagnosticNotWritten, FailureReason.Refused),
        Spec(RefusalCodes.WindowDiagnosticUnreadable, FailureReason.Refused),
        Spec(RefusalCodes.WindowDifferenceMakeFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowDifferenceReadFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowDryRunFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowNoProject, FailureReason.Refused),
        Spec(RefusalCodes.WindowOpenFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowOpenProposalFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowProposalDocumentReadFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowRefreshCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.WindowRefreshFailed, FailureReason.Refused),
        Spec(RefusalCodes.WindowRefreshUnavailable, FailureReason.Refused),
    ];
}
