namespace SIL.Motif.Commands.Requests;

public sealed record ShowConfigRequest(string FwDataPath, string ProductVersion);

public sealed record ListReportKindsRequest();

public sealed record ProduceReportRequest(
    string FwDataPath, string ProductVersion, string AssessmentId, string Kind, string? Word, string? Text);

public sealed record ProduceComparisonRequest(
    string FwDataPath, string ProductVersion, string FromAssessmentId, string ToAssessmentId);
