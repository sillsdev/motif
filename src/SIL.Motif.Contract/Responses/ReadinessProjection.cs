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
    bool Forced)
{
    /// <summary>
    /// The entry of <see cref="Reasons"/> that reports dropped grammar, so a reader that words reasons for a
    /// person can replace it; <see langword="null"/> when none does. Not part of the serialized form.
    /// </summary>
    [JsonIgnore]
    public string? DroppedGrammarReason { get; init; }

    /// <summary>The dropped grammar the Proposal adds or edits, when it made the Proposal not ready.</summary>
    [JsonIgnore]
    public DroppedGrammar? DroppedGrammar { get; init; }
}
