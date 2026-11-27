using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsApply() =>
    [
        Spec(RefusalCodes.ApplyAppliedContentMismatch, FailureReason.Refused),
        Spec(RefusalCodes.ApplyChangeNoLongerFits, FailureReason.Refused),
        Spec(RefusalCodes.ApplyChangeUncertain, FailureReason.Refused),
        Spec(RefusalCodes.ApplyChangesChanged, FailureReason.Refused),
        Spec(RefusalCodes.ApplyDrift, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.ApplyDroppedGrammar, FailureReason.Refused),
        Spec(RefusalCodes.ApplyDryRunMissing, FailureReason.Refused),
        Spec(RefusalCodes.ApplyAutonomousForce, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ApplyMessageInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ApplyMessageRequired, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ApplyNoteUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.ApplyPendingChanges, FailureReason.Refused),
        Spec(RefusalCodes.ApplyGateUndeclared, FailureReason.Refused),
        Spec(RefusalCodes.ApplyTrialGate, FailureReason.Refused),
        Spec(RefusalCodes.ApplyNotReady, FailureReason.Refused),
        Spec(RefusalCodes.ApplyNotSaved, FailureReason.Refused),
        Spec(RefusalCodes.ApplyPreconditionFailed, FailureReason.Refused),
        Spec(RefusalCodes.ApplyProjectInUse, FailureReason.Busy),
        Spec(RefusalCodes.ApplyReconciliationNeeded, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.ApplyRegression, FailureReason.Refused),
        Spec(RefusalCodes.ApplyReopenFailed, FailureReason.Refused),
        Spec(RefusalCodes.ApplyInProgress, FailureReason.Busy),
        Spec(RefusalCodes.ApplyTrialMissing, FailureReason.Refused),
        Spec(RefusalCodes.ApplyTrialStale, FailureReason.Refused),
        Spec(RefusalCodes.ApplyReceiptInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.ApplyReceiptStale, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesApply() =>
    [
        RefusalCodes.ApplyAppliedContentMismatch,
        RefusalCodes.ApplyChangeNoLongerFits,
        RefusalCodes.ApplyChangeUncertain,
        RefusalCodes.ApplyChangesChanged,
        RefusalCodes.ApplyDrift,
        RefusalCodes.ApplyDroppedGrammar,
        RefusalCodes.ApplyDryRunMissing,
        RefusalCodes.ApplyPendingChanges,
        RefusalCodes.ApplyNotReady,
        RefusalCodes.ApplyNotSaved,
        RefusalCodes.ApplyPreconditionFailed,
        RefusalCodes.ApplyProjectInUse,
        RefusalCodes.ApplyReconciliationNeeded,
        RefusalCodes.ApplyRegression,
        RefusalCodes.ApplyReopenFailed,
    ];
}
