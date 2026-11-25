using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCatalog
{
    private static IEnumerable<RefusalSpec> SpecsParsimony() =>
    [
        Spec(ParsimonyRefusalCodes.AssessmentBaselineMismatch, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.AutomaticFailed, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.BundleBusy, FailureReason.Busy),
        Spec(ParsimonyRefusalCodes.BundleInvalid, FailureReason.StoreInconsistent),
        Spec(ParsimonyRefusalCodes.BundleNotFound, FailureReason.NotFound),
        Spec(ParsimonyRefusalCodes.CandidateFailed, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.CandidateResultInvalid, FailureReason.StoreInconsistent),
        Spec(ParsimonyRefusalCodes.CandidateResultMissing, FailureReason.StoreInconsistent),
        Spec(ParsimonyRefusalCodes.DecisionAlreadyStaged, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.DispositionRequestInvalid, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.DispositionRetractionRequestInvalid, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.DispositionRevisionRequestInvalid, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.DryRunEvidenceMissing, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.DryRunRequired, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.FindingAmbiguous, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.FindingNotInReport, FailureReason.NotFound),
        Spec(ParsimonyRefusalCodes.InvalidAssessment, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.InvalidCatalogRequest, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.InvalidLatestRequest, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.InvalidGroup, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.MeasureOutsideGroup, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.ParsimonySelectionNotConfigured, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.InvalidMeasure, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.InvalidScope, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.InvalidViewRequest, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.JobFailed, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.LeaseLost, FailureReason.Busy),
        Spec(ParsimonyRefusalCodes.MeasureUnavailable, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.NegativeConfirmationRequired, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.NegativeRequestInvalid, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.NoReport, FailureReason.NotFound),
        Spec(ParsimonyRefusalCodes.RecordTypeIdInvalid, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.ReportBaselineMismatch, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.ReportBundleMismatch, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.ReportDamaged, FailureReason.StoreInconsistent),
        Spec(ParsimonyRefusalCodes.ReportIdRequired, FailureReason.InvalidArgument),
        Spec(ParsimonyRefusalCodes.ReportNotFound, FailureReason.NotFound),
        Spec(ParsimonyRefusalCodes.ResultInvalid, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.ResultMissing, FailureReason.StoreInconsistent),
        Spec(ParsimonyRefusalCodes.RetirementExpectationsUnavailable, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.RunnerStartFailed, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.SelectionUnavailable, FailureReason.Refused),
        Spec(ParsimonyRefusalCodes.ViewItemNotFound, FailureReason.NotFound),
    ];
}
