using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Commands.Requests;

/// <summary>Selects a measure and its Baseline evidence for a Parsimony report.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="MeasureId">The registered Parsimony measure to run.</param>
/// <param name="EvidenceScope">The declared source of word evidence for the measure.</param>
/// <param name="AssessmentIds">The exact retained Assessments supplying evidence, when supplied.</param>
public sealed record EnqueueParsimonyReportRequest(string FwDataPath, string ProductVersion, string MeasureId,
    ParsimonyEvidenceScopeKind EvidenceScope = ParsimonyEvidenceScopeKind.DefaultSelection,
    IReadOnlyList<string>? AssessmentIds = null);

/// <summary>Selects a measure over the candidate grammar from a completed Dry Run.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="DryRunJobId">The completed Dry Run supplying the candidate grammar.</param>
/// <param name="MeasureId">The registered Parsimony measure to run.</param>
/// <param name="EvidenceScope">The declared source of word evidence for the measure.</param>
/// <param name="AssessmentIds">The exact retained Assessments supplying evidence, when supplied.</param>
public sealed record EnqueueParsimonyCandidateRequest(string FwDataPath, string ProductVersion, string DryRunJobId,
    string MeasureId, ParsimonyEvidenceScopeKind EvidenceScope = ParsimonyEvidenceScopeKind.ProjectApproved,
    IReadOnlyList<string>? AssessmentIds = null);

/// <summary>Waits for queued candidate Parsimony evidence.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="JobId">The queued job whose result to read.</param>
/// <param name="Timeout">The maximum wait for this call; the job continues after the wait ends.</param>
public sealed record WaitForParsimonyCandidateRequest(string FwDataPath, string ProductVersion, string JobId,
    TimeSpan Timeout);

/// <summary>Waits for a queued Baseline Parsimony report.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="JobId">The queued job whose result to read.</param>
/// <param name="Timeout">The maximum wait for this call; the job continues after the wait ends.</param>
public sealed record WaitForParsimonyReportRequest(
    string FwDataPath, string ProductVersion, string JobId, TimeSpan Timeout);

/// <summary>Selects one retained Parsimony report.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="ReportId">The exact stored Parsimony report identity.</param>
public sealed record ShowParsimonyReportRequest(string FwDataPath, string ProductVersion, string ReportId);

/// <summary>Selects the newest retained Parsimony report.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
public sealed record ReadLatestParsimonyReportRequest(string FwDataPath, string ProductVersion);

/// <summary>Records an advisory disposition over the exact evidence of a retained finding.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="ReportId">The exact stored Parsimony report identity.</param>
/// <param name="FindingId">The exact finding identity within that report.</param>
/// <param name="Disposition">The advisory disposition to record for the finding.</param>
/// <param name="RecordTypeId">The project's Notebook record type for the advisory record.</param>
/// <param name="DraftName">The mutable Draft receiving the semantic intent.</param>
/// <param name="Reason">The person's reason for the advisory disposition, when supplied.</param>
/// <param name="Question">The question to retain for later review, when supplied.</param>
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

/// <summary>Authors a revision of an advisory disposition in a Draft.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="DraftName">The mutable Draft receiving the semantic intent.</param>
/// <param name="IntentJson">The closed semantic intent consumed by the typed composer.</param>
public sealed record ReviseParsimonyDispositionRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson);

/// <summary>Authors a retraction of an advisory disposition in a Draft.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="DraftName">The mutable Draft receiving the semantic intent.</param>
/// <param name="IntentJson">The closed semantic intent consumed by the typed composer.</param>
public sealed record RetractParsimonyDispositionRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string IntentJson);

public sealed record ListNotebookRecordTypesRequest(string FwDataPath, string ProductVersion);

public sealed record ReadParsimonyExpectationsRequest(string FwDataPath, string ProductVersion);

/// <summary>Selects one staged rule-based allomorph retirement Draft for review.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="DraftId">The exact staged Draft Proposal identity.</param>
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

/// <summary>Selects a declared Parsimony evidence view.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="Query">The declared named Parsimony view and its filters.</param>
public sealed record ReadParsimonyViewRequest(
    string FwDataPath,
    string ProductVersion,
    ParsimonyNamedViewRequest Query);
