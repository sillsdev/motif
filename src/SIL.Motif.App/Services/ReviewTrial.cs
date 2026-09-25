namespace SIL.Motif.App.Services;

/// <summary>Measures the changes in one exact pending revision against the words they touch.</summary>
public sealed record ReviewTrialRequest(
    string ProjectPath, string DraftId, string Revision, IReadOnlyList<string> Words,
    string? CurrentCorrectnessAssessmentId = null);

/// <summary>The numbers returned by the Trial's recorded correctness report.</summary>
public sealed record ReviewTrialResult(
    string JobId, string Revision, string NumbersText, bool EvidenceComplete);

/// <summary>The number of touched words whose Trial work has finished.</summary>
public sealed record ReviewTrialProgress(int Completed, int Total, string? CurrentWord);

/// <summary>Applies only the pending revision that the person measured and reviewed.</summary>
public sealed record ReviewApplyRequest(string ProjectPath, string DraftId, string Revision);
