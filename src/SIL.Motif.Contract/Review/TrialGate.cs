using System.Text.Json.Serialization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Review;

/// <summary>How a finished Trial reads to a script: nothing lost, a word lost, or not fully measured.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<TrialGateState>))]
public enum TrialGateState
{
    [JsonStringEnumMemberName("green")] Green,
    [JsonStringEnumMemberName("loss")] Loss,
    [JsonStringEnumMemberName("unmeasured")] Unmeasured,
}

/// <summary>
/// The gate of a Trial report. <see cref="TrialGateState.Loss"/> means a measured word went from analysed to
/// no analysis, or lost a confirmed reading, and the author did not list it as an expected loss; the gate reads the retained evidence, so
/// the review sets chosen for the Trial do not change it.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialGate(
    [property: JsonRequired] TrialGateState State,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Reason,
    [property: JsonRequired] long LostWords,
    [property: JsonRequired] IReadOnlyList<string> ExpectedLosses,
    [property: JsonRequired] IReadOnlyList<string> UnexpectedLosses,
    [property: JsonRequired] IReadOnlyList<string> StoppedWords);

/// <summary>A named stopped case with its producer-attributed cause on one side of a Trial.</summary>
public sealed record TrialStoppedWord(string CaseKey, string Word, Side Side,
    IReadOnlyList<string> Reasons, IReadOnlyList<string> Details);
