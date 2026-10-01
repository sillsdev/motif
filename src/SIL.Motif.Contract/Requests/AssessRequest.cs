using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Which project to measure, and which Selection to measure it over (design decision 4). A per-word time limit,
/// in milliseconds, overrides the project's configured one for this run only; <see langword="null"/> keeps it.
/// <c>ReplaceAssessmentId</c> names the complete run whose main answers an explicit reparse replaces;
/// absent for exploration and ordinary new runs.
/// </summary>
public sealed record AssessRequest(
    string ProjectPath,
    SelectionRequest? Selection = null,
    int? PerWordLimitMs = null,
    StepCap? PerWordStepLimit = null,
    string? ReplaceAssessmentId = null);
