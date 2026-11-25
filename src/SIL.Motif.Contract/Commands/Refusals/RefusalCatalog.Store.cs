using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsStore() =>
    [
        Spec(RefusalCodes.StoreBackupNoStore, FailureReason.NotFound),
        Spec(RefusalCodes.StoreBackupDestinationExists, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StoreRestoreInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StoreBackupDestinationOwned, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StoreRestoreNewerSchema, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreRestoreChecksumMismatch, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreRestoreOtherProject, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreInconsistent, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.StoreOtherProject, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreOtherVersion, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreRelocateConfirmationRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.StoreRelocateIdentityUnproven, FailureReason.Refused,
            NextStep.PersonStep()),
        Spec(RefusalCodes.StoreRelocateNoStore, FailureReason.NotFound),
        Spec(RefusalCodes.StoreUnsupported, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesStore() =>
    [
        RefusalCodes.StoreInconsistent,
        RefusalCodes.StoreOtherProject,
        RefusalCodes.StoreOtherVersion,
        RefusalCodes.StoreUnsupported,
    ];
}
