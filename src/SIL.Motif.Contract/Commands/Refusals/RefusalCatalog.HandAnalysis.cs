using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsHandAnalysis() =>
    [
        Spec(RefusalCodes.HandAnalysisStale, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisAmbiguousWordform, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisInvalidLink, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisTypeMismatch, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisDisapproved, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisRequestIdReused, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisBusy, FailureReason.Busy),
        Spec(RefusalCodes.HandAnalysisStoreUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisOutcomeUnknown, FailureReason.StoreInconsistent),
        Spec(RefusalCodes.HandAnalysisSnapshotExpired, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisIndexUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisSourceUnavailable, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisInvalidBuffer, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HandAnalysisIncompleteReferences, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisStaleReadSet, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisReferenced, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisDeletionPlanMismatch, FailureReason.Refused),
        Spec(RefusalCodes.HandAnalysisUnsupportedOperation, FailureReason.InvalidArgument),
        Spec(RefusalCodes.HandAnalysisIdentityMismatch, FailureReason.Refused),
    ];
}
