using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsTrial() =>
    [
        Spec(RefusalCodes.TrialChangesChanged, FailureReason.Refused),
        Spec(RefusalCodes.TrialMeasurementIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.TrialLatestTooLarge, FailureReason.Refused),
        Spec(RefusalCodes.TrialNothingPending, FailureReason.Refused),
        Spec(RefusalCodes.TrialProposalGone, FailureReason.NotFound),
        Spec(RefusalCodes.TrialReviewBaselineChanged, FailureReason.Refused),
        Spec(RefusalCodes.TrialExportUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.TrialDeltaRequestInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TrialDeltaIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.TrialDeltaManifestUnwritable, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TrialReviewInputUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.TrialNoBaseSampleInvalid, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TrialWorkingSetNotAGate, FailureReason.Refused),
        Spec(RefusalCodes.TrialWorkingSetListsDiffer, FailureReason.Refused),
        Spec(RefusalCodes.TrialTraceUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.TrialUpdateInProgress, FailureReason.Busy),
        Spec(RefusalCodes.TrialTouchedNeedsWords, FailureReason.InvalidArgument),
        Spec(RefusalCodes.TrialTouchedNoBaseline, FailureReason.NotFound),
        Spec(RefusalCodes.TrialTouchedNoObjects, FailureReason.NotFound),
        Spec(RefusalCodes.TrialNone, FailureReason.NotFound),
        Spec(RefusalCodes.TrialScratchGone, FailureReason.NotFound),
    ];

    private static IEnumerable<string> WindowCodesTrial() =>
    [
        RefusalCodes.TrialChangesChanged,
        RefusalCodes.TrialMeasurementIncomplete,
        RefusalCodes.TrialNone,
        RefusalCodes.TrialNothingPending,
        RefusalCodes.TrialProposalGone,
        RefusalCodes.TrialReviewBaselineChanged,
        RefusalCodes.TrialReviewInputUnavailable,
        RefusalCodes.TrialTraceUnavailable,
        RefusalCodes.TrialScratchGone,
        RefusalCodes.TrialUpdateInProgress,
    ];
}
