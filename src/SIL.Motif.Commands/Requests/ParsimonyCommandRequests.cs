using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Commands.Requests;

public sealed record EnqueueParsimonyReportRequest(string FwDataPath, string ProductVersion, string MeasureId,
    ParsimonyEvidenceScopeKind EvidenceScope = ParsimonyEvidenceScopeKind.DefaultSelection,
    IReadOnlyList<string>? AssessmentIds = null);

public sealed record EnqueueParsimonyCandidateRequest(string FwDataPath, string ProductVersion, string DryRunJobId,
    string MeasureId, ParsimonyEvidenceScopeKind EvidenceScope = ParsimonyEvidenceScopeKind.ProjectApproved,
    IReadOnlyList<string>? AssessmentIds = null);

public sealed record WaitForParsimonyCandidateRequest(string FwDataPath, string ProductVersion, string JobId,
    TimeSpan Timeout);

public sealed record WaitForParsimonyReportRequest(
    string FwDataPath, string ProductVersion, string JobId, TimeSpan Timeout);

public sealed record ShowParsimonyReportRequest(string FwDataPath, string ProductVersion, string ReportId);

public sealed record ReadLatestParsimonyReportRequest(string FwDataPath, string ProductVersion);

public sealed record RecordParsimonyDispositionFromFindingRequest(
    string FwDataPath,
    string ProductVersion,
    string ReportId,
    string FindingId,
    string Disposition,
    string RecordTypeId,
    string DraftName,
    string? Reason = null,
    string? Question = null);

public sealed record ReviseParsimonyDispositionRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson);

public sealed record RetractParsimonyDispositionRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson);

public sealed record ListNotebookRecordTypesRequest(string FwDataPath, string ProductVersion);

public sealed record ReadParsimonyExpectationsRequest(string FwDataPath, string ProductVersion);

public sealed record ReadRetirementReviewRequest(string FwDataPath, string ProductVersion, string DraftId);

public sealed record ConfirmReviewedNegativeRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson,
    string? Confirmation);

public sealed record RetractReviewedNegativeRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson,
    string? Confirmation);

public sealed record ListParsimonyMeasuresRequest(string FwDataPath, string ProductVersion);

public sealed record ReadParsimonyViewRequest(
    string FwDataPath,
    string ProductVersion,
    ParsimonyNamedViewRequest Query);
