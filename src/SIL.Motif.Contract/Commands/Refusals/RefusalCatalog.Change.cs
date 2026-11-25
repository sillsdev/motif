using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsChange() =>
    [
        Spec(RefusalCodes.ChangeAnalysisIdentityRequired, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAnalysisOwnerInvalid, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentIncomplete, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentKind, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentMissing, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentRequired, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentStale, FailureReason.Refused),
        Spec(RefusalCodes.ChangeAssessmentWordMissing, FailureReason.Refused),
        Spec(RefusalCodes.ChangeBaselineMissing, FailureReason.Refused),
        Spec(RefusalCodes.ChangeCannotCompose, FailureReason.Refused),
        Spec(RefusalCodes.ChangeInvalidIdentity, FailureReason.Refused),
        Spec(RefusalCodes.ChangeNoEffect, FailureReason.Refused),
        Spec(RefusalCodes.ChangeNotFound, FailureReason.Refused),
        Spec(RefusalCodes.ChangeProjectSaving, FailureReason.Busy,
            NextStep.Repeat()),
        Spec(RefusalCodes.ChangeOccurrenceUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.ChangeOccurrenceWordformMismatch, FailureReason.Refused),
        Spec(RefusalCodes.ChangeReconfirmNotAllowed, FailureReason.Refused),
        Spec(RefusalCodes.ChangeReconfirmUnneeded, FailureReason.Refused),
        Spec(RefusalCodes.ChangeReadingMissing, FailureReason.Refused),
        Spec(RefusalCodes.ChangeRefreshRequired, FailureReason.Refused),
        Spec(RefusalCodes.ChangeRevisionConflict, FailureReason.Refused),
        Spec(RefusalCodes.ChangeScopeInvalid, FailureReason.Refused),
        Spec(RefusalCodes.ChangeSlotOccupied, FailureReason.Refused),
        Spec(RefusalCodes.ChangeStoredAnalysisMissing, FailureReason.Refused),
        Spec(RefusalCodes.ChangeTextUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.ChangeWordformAmbiguous, FailureReason.Refused),
        Spec(RefusalCodes.ChangeWordformChanged, FailureReason.Refused),
        Spec(RefusalCodes.ChangeWordformMissing, FailureReason.Refused),
    ];

    private static IEnumerable<string> WindowCodesChange() =>
    [
        RefusalCodes.ChangeAnalysisIdentityRequired,
        RefusalCodes.ChangeAnalysisOwnerInvalid,
        RefusalCodes.ChangeAssessmentIncomplete,
        RefusalCodes.ChangeAssessmentKind,
        RefusalCodes.ChangeAssessmentMissing,
        RefusalCodes.ChangeAssessmentRequired,
        RefusalCodes.ChangeAssessmentStale,
        RefusalCodes.ChangeAssessmentWordMissing,
        RefusalCodes.ChangeBaselineMissing,
        RefusalCodes.ChangeCannotCompose,
        RefusalCodes.ChangeInvalidIdentity,
        RefusalCodes.ChangeNoEffect,
        RefusalCodes.ChangeNotFound,
        RefusalCodes.ChangeOccurrenceUnavailable,
        RefusalCodes.ChangeOccurrenceWordformMismatch,
        RefusalCodes.ChangeProjectSaving,
        RefusalCodes.ChangeReadingMissing,
        RefusalCodes.ChangeReconfirmNotAllowed,
        RefusalCodes.ChangeReconfirmUnneeded,
        RefusalCodes.ChangeRefreshRequired,
        RefusalCodes.ChangeRevisionConflict,
        RefusalCodes.ChangeScopeInvalid,
        RefusalCodes.ChangeSlotOccupied,
        RefusalCodes.ChangeStoredAnalysisMissing,
        RefusalCodes.ChangeTextUnavailable,
        RefusalCodes.ChangeWordformAmbiguous,
        RefusalCodes.ChangeWordformChanged,
        RefusalCodes.ChangeWordformMissing,
    ];
}
