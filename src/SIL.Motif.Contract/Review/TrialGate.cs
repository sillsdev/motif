using System.Text.Json;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.HandAnalysis;
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
    /// <summary>
    /// The canonical Reading that lost copies, shared with the export. Present on a retirement row for a waived word,
    /// where <see cref="ReadingIdentity"/> is its <see cref="ReadingSignature.Id"/> and the assertion fields name any
    /// frozen expectation the lost Reading matched. Rows with a signature (the retirement multiset) and rows without
    /// one (confirmed losses of frozen expectations) describe the same loss from two sides: a gate must read each kind
    /// on its own and must not sum across the two kinds.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadingSignature? Signature { get; init; }

    /// <summary>The copies of <see cref="Signature"/> the parser emitted before this case and no longer emits after it.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Count { get; init; }

    /// <summary>The lexicon-backed Reading copies the case still emits after the change, whatever their signature.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? RemainingReadingCount { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (Word is null || CaseKey is null || ReadingIdentity is null || AssertionIds is null ||
            AssertionIds.Any(item => item is null)) throw new JsonException("Invalid TrialLostReading.");
        var counted = Signature is not null;
        if (counted != (Count is not null) || counted != (RemainingReadingCount is not null) ||
            Count is < 1 or > 9007199254740991L || RemainingReadingCount is < 0 or > 9007199254740991L)
            throw new JsonException("Invalid TrialLostReading retirement counts.");
    }
}

/// <summary>
/// States, bound to one saved report, whether the signature rows of <see cref="TrialLosses.NewlyLostReadings"/> cover the
/// full retirement multiset of the waived words across every measured case. A script may rely on the rows only when
/// <see cref="Complete"/> is true.
/// </summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialRetirementAttestation(
    [property: JsonRequired] string ReportDigest,
    [property: JsonRequired] bool Complete,
    [property: JsonRequired] IReadOnlyList<string> WaivedWords,
    [property: JsonRequired] long CaseCount,
    [property: JsonRequired] long LostCopies,
    [property: JsonRequired] long RowCount,
    [property: JsonRequired] IReadOnlyList<string> Limitations) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>The cursor for the next page of signature rows. Absent on the stored report and on the last page; set by a read.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NextCursor { get; init; }

    void IJsonOnDeserialized.OnDeserialized() => Validate();
    void IJsonOnSerializing.OnSerializing() => Validate();

    private void Validate()
    {
        if (ReportDigest is null || WaivedWords is null || WaivedWords.Any(item => item is null) ||
            Limitations is null || Limitations.Any(item => item is null) ||
            CaseCount is < 0 or > 9007199254740991L || LostCopies is < 0 or > 9007199254740991L ||
            RowCount is < 0 or > 9007199254740991L ||
            Complete && Limitations.Count > 0)
            throw new JsonException("Invalid TrialRetirementAttestation.");
    }
}

/// <summary>Exact named losses retained with the summary when optional detail rows are unavailable.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TrialLosses(
    [property: JsonRequired] IReadOnlyList<string> NewNoParseWords,
    [property: JsonRequired] IReadOnlyList<string> NewApprovedLossWords,
    [property: JsonRequired] IReadOnlyList<TrialLostReading> NewlyLostReadings) : IJsonOnDeserialized, IJsonOnSerializing
{
    /// <summary>Present when the Trial waived words; see <see cref="TrialRetirementAttestation"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TrialRetirementAttestation? RetirementAttestation { get; init; }

    /// <summary>
    /// The distinct spellings affected by any measured loss. A retirement row that matched no frozen expectation records
    /// a waived word's lost copy; it is not itself a confirmed loss and adds no word.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> Words => NewNoParseWords.Concat(NewApprovedLossWords)
        .Concat(NewlyLostReadings.Where(reading => reading.Signature is null || reading.Approved || reading.ReferenceConfirmed)
            .Select(reading => reading.Word)).Distinct(StringComparer.Ordinal)
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
