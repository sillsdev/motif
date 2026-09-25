namespace SIL.Motif.Contract.Requests;

/// <summary>Reads stored timing rows for a selected set of words in one Assessment.</summary>
public sealed record TimingRequest(
    string ProjectPath,
    string? AssessmentId = null,
    string WordSet = "all",
    string By = "kind",
    string? Rule = null,
    int Top = 10,
    IReadOnlyList<string>? ExplicitWords = null,
    IReadOnlyList<string>? OverrideAssessmentIds = null);
