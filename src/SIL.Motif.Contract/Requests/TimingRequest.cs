using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Requests;

/// <summary>Reads stored timing rows for a selected set of words in one Assessment.</summary>
/// <param name="ProjectPath">The recorded project path.</param>
/// <param name="AssessmentId">The retained Assessment to read; null selects the command's default Assessment.</param>
/// <param name="WordSet">The declared subset of measured words to select.</param>
/// <param name="By">The timing grouping to read.</param>
/// <param name="Rule">The exact timing rule identity, when supplied.</param>
/// <param name="Top">The maximum number of timing rows.</param>
/// <param name="ExplicitWords">The exact word forms to select, when supplied.</param>
/// <param name="OverrideAssessmentIds">The retained Assessments to use instead of the default, when supplied.</param>
public sealed record TimingRequest(
    string ProjectPath,
    string? AssessmentId = null,
    string WordSet = "all",
    string By = "kind",
    TraceTimingKey? Rule = null,
    int Top = 10,
    IReadOnlyList<string>? ExplicitWords = null,
    IReadOnlyList<string>? OverrideAssessmentIds = null);
