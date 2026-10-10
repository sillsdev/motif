namespace SIL.Motif.Commands.Requests;

public sealed record ShowConfigRequest(string FwDataPath, string ProductVersion);

public sealed record ListReportKindsRequest();

/// <summary>Selects a registered report over a retained Assessment.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="AssessmentId">The retained Assessment to read; null selects the command's default Assessment.</param>
/// <param name="Kind">The declared report kind or diagnostic code to select.</param>
/// <param name="Word">The exact word form to read.</param>
/// <param name="Text">The report's text selector, when supplied.</param>
public sealed record ProduceReportRequest(
    string FwDataPath, string ProductVersion, string? AssessmentId, string Kind, string? Word, string? Text);

/// <summary>Selects two retained Assessments to compare.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="FromAssessmentId">The earlier retained Assessment in the comparison.</param>
/// <param name="ToAssessmentId">The later retained Assessment in the comparison.</param>
public sealed record ProduceComparisonRequest(
    string FwDataPath, string ProductVersion, string FromAssessmentId, string ToAssessmentId);
