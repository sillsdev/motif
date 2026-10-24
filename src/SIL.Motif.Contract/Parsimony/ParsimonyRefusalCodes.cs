namespace SIL.Motif.Contract.Parsimony;

/// <summary>
/// Every refusal code a Parsimony command, the job runner, the MCP tools and the window can name, spelled once.
/// The values are wire strings read by the CLI JSON, MCP and external callers, so a rename is a breaking change.
/// </summary>
public static class ParsimonyRefusalCodes
{
    public const string AssessmentBaselineMismatch = "parsimony.assessment-baseline-mismatch";
    public const string AutomaticFailed = "parsimony.automatic-failed";
    public const string BundleBusy = "parsimony.bundle-busy";
    public const string BundleInvalid = "parsimony.bundle-invalid";
    public const string BundleNotFound = "parsimony.bundle-not-found";
    public const string CandidateFailed = "parsimony.candidate-failed";
    public const string CandidateResultInvalid = "parsimony.candidate-result-invalid";
    public const string CandidateResultMissing = "parsimony.candidate-result-missing";
    public const string DecisionAlreadyStaged = "parsimony.decision-already-staged";
    public const string DispositionRequestInvalid = "parsimony.disposition-request-invalid";
    public const string DispositionRetractionRequestInvalid = "parsimony.disposition-retraction-request-invalid";
    public const string DispositionRevisionRequestInvalid = "parsimony.disposition-revision-request-invalid";
    public const string DryRunEvidenceMissing = "parsimony.dry-run-evidence-missing";
    public const string DryRunRequired = "parsimony.dry-run-required";
    public const string FindingAmbiguous = "parsimony.finding-ambiguous";
    public const string FindingNotInReport = "parsimony.finding-not-in-report";
    public const string InvalidAssessment = "parsimony.invalid-assessment";
    public const string InvalidCatalogRequest = "parsimony.invalid-catalog-request";
    public const string InvalidLatestRequest = "parsimony.invalid-latest-request";
    public const string InvalidMeasure = "parsimony.invalid-measure";
    public const string InvalidScope = "parsimony.invalid-scope";
    public const string InvalidViewRequest = "parsimony.invalid-view-request";
    public const string JobFailed = "parsimony.job-failed";
    public const string LeaseLost = "parsimony.lease-lost";
    public const string MeasureUnavailable = "parsimony.measure-unavailable";
    public const string NegativeConfirmationRequired = "parsimony.negative-confirmation-required";
    public const string NegativeRequestInvalid = "parsimony.negative-request-invalid";
    public const string NoReport = "parsimony.no-report";
    public const string RecordTypeIdInvalid = "parsimony.record-type-id-invalid";
    public const string ReportBaselineMismatch = "parsimony.report-baseline-mismatch";
    public const string ReportBundleMismatch = "parsimony.report-bundle-mismatch";
    public const string ReportDamaged = "parsimony.report-damaged";
    public const string ReportIdRequired = "parsimony.report-id-required";
    public const string ReportNotFound = "parsimony.report-not-found";
    public const string ResultInvalid = "parsimony.result-invalid";
    public const string ResultMissing = "parsimony.result-missing";
    public const string RetirementExpectationsUnavailable = "parsimony.retirement-expectations-unavailable";
    public const string RunnerStartFailed = "parsimony.runner-start-failed";
    public const string SelectionUnavailable = "parsimony.selection-unavailable";
    public const string ViewItemNotFound = "parsimony.view-item-not-found";
}
