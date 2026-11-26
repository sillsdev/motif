using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsInitialization() =>
    [
        Spec(RefusalCodes.InitializationAnalysisWritingSystemRequired, FailureReason.Refused),
        Spec(RefusalCodes.InitializationCancelled, FailureReason.Cancelled),
        Spec(RefusalCodes.InitializationConfirmationRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.InitializationNotSaved, FailureReason.Refused),
        Spec(RefusalCodes.InitializationOutcomeUnknown, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.InitializationRecoveryCopyFailed, FailureReason.Refused),
    ];
}
