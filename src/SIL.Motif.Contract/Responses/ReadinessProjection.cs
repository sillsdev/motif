using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The Readiness decision recorded with an Apply result.</summary>
/// <param name="IsReady">Whether the Proposal met Readiness without using <c>--force</c>.</param>
/// <param name="CorrectnessAssessmentRequired">Whether a Correctness Assessment was part of Readiness.</param>
/// <param name="CorrectnessAssessmentExempt">Whether verified Dry Run effects qualified for the judgment-only exemption.</param>
/// <param name="CorrectnessAssessmentExemptionReason">Why the Dry Run effects made a Correctness Assessment unnecessary.</param>
/// <param name="Reasons">The reasons Readiness was not met, including any reasons overridden by <c>--force</c>.</param>
/// <param name="Forced">Whether <c>--force</c> allowed Apply despite one or more Readiness reasons.</param>
public sealed record ReadinessProjection(
    bool IsReady,
    bool CorrectnessAssessmentRequired,
    bool CorrectnessAssessmentExempt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrectnessAssessmentExemptionReason,
    IReadOnlyList<string> Reasons,
    bool Forced);
