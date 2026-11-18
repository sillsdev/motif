using System.Text.Json;
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
    [property: JsonRequired] IReadOnlyList<string> StoppedWords) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (!Enum.IsDefined(State) || LostWords is < 0 or > 9007199254740991L ||
            ExpectedLosses is null || ExpectedLosses.Any(item => item is null) ||
            UnexpectedLosses is null || UnexpectedLosses.Any(item => item is null) ||
            StoppedWords is null || StoppedWords.Any(item => item is null))
            throw new JsonException("Invalid TrialGate.");
    }
}

/// <summary>A named stopped case with its producer-attributed cause on one side of a Trial.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialStoppedWord(
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired] string Word,
    [property: JsonRequired] Side Side,
    [property: JsonRequired] IReadOnlyList<string> Reasons,
    [property: JsonRequired] IReadOnlyList<string> Details) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>The producer outcome, including an observed stop with no morphology packet.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Outcome { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (CaseKey is null || Word is null || !Enum.IsDefined(Side) || Reasons is null ||
            Reasons.Any(item => item is null) || Details is null || Details.Any(item => item is null))
            throw new JsonException("Invalid TrialStoppedWord.");
    }
}

/// <summary>A named reading lost from a word, including its frozen expectation identities.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialLostReading(
    [property: JsonRequired] string Word,
    [property: JsonRequired] string CaseKey,
    [property: JsonRequired] string ReadingIdentity,
    [property: JsonRequired] bool Approved,
    [property: JsonRequired] bool ReferenceConfirmed,
    [property: JsonRequired] IReadOnlyList<string> AssertionIds) : IJsonOnDeserialized, IJsonOnSerializing
{
    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Word is null || CaseKey is null || ReadingIdentity is null || AssertionIds is null ||
            AssertionIds.Any(item => item is null)) throw new JsonException("Invalid TrialLostReading.");
    }
}

/// <summary>Exact named losses retained with the summary when optional detail rows are unavailable.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialLosses(
    [property: JsonRequired] IReadOnlyList<string> NewNoParseWords,
    [property: JsonRequired] IReadOnlyList<string> NewApprovedLossWords,
    [property: JsonRequired] IReadOnlyList<TrialLostReading> NewlyLostReadings) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>The distinct spellings affected by any measured loss.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Words => NewNoParseWords.Concat(NewApprovedLossWords)
        .Concat(NewlyLostReadings.Select(reading => reading.Word)).Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal).ToArray();

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (NewNoParseWords is null || NewNoParseWords.Any(item => item is null) ||
            NewApprovedLossWords is null || NewApprovedLossWords.Any(item => item is null) ||
            NewlyLostReadings is null || NewlyLostReadings.Any(item => item is null))
            throw new JsonException("Invalid TrialLosses.");
    }
}
