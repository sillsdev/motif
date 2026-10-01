using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>What kind of thing the inspector shows.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InspectorSubjectKind>))]
public enum InspectorSubjectKind
{
    /// <summary>One morpheme: an allomorph, its grammatical info, or both.</summary>
    [JsonStringEnumMemberName("morpheme")]
    Morpheme,

    /// <summary>A rule PanGloss runs, by the kind and key its statistics record it under.</summary>
    [JsonStringEnumMemberName("rule")]
    Rule,

    /// <summary>A template slot.</summary>
    [JsonStringEnumMemberName("slot")]
    Slot,

    /// <summary>A phonological environment.</summary>
    [JsonStringEnumMemberName("environment")]
    Environment,

    /// <summary>An inflection feature or one of its values.</summary>
    [JsonStringEnumMemberName("feature")]
    Feature,

    /// <summary>A grammar finding, by its code and the object it names.</summary>
    [JsonStringEnumMemberName("warning")]
    Warning,
}

/// <summary>
/// One thing to inspect, named by explicit identity. Each kind reads only its own fields; a label or gloss is for
/// display and is never matched. Identity quality says how far the identity can be trusted: <c>authored</c> for a
/// FieldWorks GUID, <c>structural</c> or <c>synthetic</c> for a parser locator, <c>unknown</c> when not recorded.
/// </summary>
/// <param name="Kind">What kind of thing the subject is.</param>
public sealed record InspectorSubject(InspectorSubjectKind Kind)
{
    /// <summary>A morpheme's allomorph GUID, or <see langword="null"/> when the subject names none.</summary>
    public string? AllomorphId { get; init; }

    /// <summary>A morpheme's grammatical info GUID, or <see langword="null"/> when the subject names none.</summary>
    public string? GrammaticalInfoId { get; init; }

    /// <summary>A rule's kind and key in PanGloss's statistics, such as <c>phon_rule</c> and its GUID.</summary>
    public TraceTimingKey? TimingKey { get; init; }

    /// <summary>A slot's, environment's or feature's GUID, or the object a warning names.</summary>
    public string? ObjectId { get; init; }

    /// <summary>A warning's diagnostic code.</summary>
    public string? WarningCode { get; init; }

    /// <summary>How far the identity can be trusted: <c>authored</c>, <c>structural</c>, <c>synthetic</c> or <c>unknown</c>.</summary>
    public string IdentityQuality { get; init; } = "authored";

    /// <summary>What the window calls the subject; display only.</summary>
    public string? Label { get; init; }

    /// <summary>A morpheme's gloss; display only.</summary>
    public string? Gloss { get; init; }

    /// <summary>A morpheme by its allomorph, its grammatical info, or both; <see langword="null"/> when it names neither.</summary>
    public static InspectorSubject? Morpheme(string? allomorphId, string? grammaticalInfoId, string? label = null,
        string? gloss = null) =>
        allomorphId is null && grammaticalInfoId is null ? null : new InspectorSubject(InspectorSubjectKind.Morpheme)
        {
            AllomorphId = allomorphId,
            GrammaticalInfoId = grammaticalInfoId,
            Label = label,
            Gloss = gloss is { Length: > 0 } ? gloss : null,
        };

    /// <summary>A morpheme of a reading, by its allomorph and grammatical info.</summary>
    public static InspectorSubject? Morpheme(ParserReadingMorph morph) =>
        morph is null ? null : Morpheme(morph.AllomorphId, morph.GrammaticalInfoId, morph.Form, morph.Gloss);

    /// <summary>A rule by the kind and key PanGloss's statistics record it under, with how far that key can be trusted.</summary>
    public static InspectorSubject Rule(TraceTimingKey key, string? label = null, string identityQuality = "authored") =>
        new(InspectorSubjectKind.Rule) { TimingKey = key, Label = label, IdentityQuality = identityQuality };
}

/// <summary>Whether a section of an inspection could be read, and if not, why.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InspectorSectionStatus>))]
public enum InspectorSectionStatus
{
    /// <summary>The section was read; its value may still be an empty list, which is a real answer.</summary>
    [JsonStringEnumMemberName("available")]
    Available,

    /// <summary>The source the section reads from is missing, such as no Parse all words yet.</summary>
    [JsonStringEnumMemberName("absent")]
    Absent,

    /// <summary>The request did not ask for the section.</summary>
    [JsonStringEnumMemberName("not_requested")]
    NotRequested,

    /// <summary>Motif doesn't read this section for this kind of subject.</summary>
    [JsonStringEnumMemberName("unsupported")]
    Unsupported,
}

/// <summary>One separately sourced section of an inspection: its status, its value when available, and why not.</summary>
/// <param name="Status">Whether the section was read.</param>
public sealed record InspectorSection<T>(InspectorSectionStatus Status)
{
    /// <summary>The section's content; present only when <see cref="Status"/> is available.</summary>
    public T? Value { get; init; }

    /// <summary>Why the section isn't available, in the window's words; <see langword="null"/> when it is.</summary>
    public string? Reason { get; init; }

    /// <summary>An available section holding <paramref name="value"/>.</summary>
    public static InspectorSection<T> Of(T value) => new(InspectorSectionStatus.Available) { Value = value };

    /// <summary>A section that isn't available, with the status and reason.</summary>
    public static InspectorSection<T> Not(InspectorSectionStatus status, string reason) => new(status) { Reason = reason };
}

/// <summary>Whether the subject's identity was found in the Baseline, and as one consistent object.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<InspectorResolution>))]
public enum InspectorResolution
{
    /// <summary>Every id the subject gives names an object of the right kind, and together they name one object.</summary>
    [JsonStringEnumMemberName("resolved")]
    Resolved,

    /// <summary>An id the subject gives is not in the Baseline, or names another kind of object.</summary>
    [JsonStringEnumMemberName("not_in_baseline")]
    NotInBaseline,

    /// <summary>The ids are each found but name different objects, such as an allomorph of one entry and another's grammatical info.</summary>
    [JsonStringEnumMemberName("contradictory")]
    Contradictory,

    /// <summary>The identity is not a FieldWorks GUID, so the Baseline cannot be asked about it.</summary>
    [JsonStringEnumMemberName("not_authored")]
    NotAuthored,

    /// <summary>No Baseline has been captured, so nothing could be looked up.</summary>
    [JsonStringEnumMemberName("no_baseline")]
    NoBaseline,

    /// <summary>Motif doesn't look up this kind of subject in the Baseline.</summary>
    [JsonStringEnumMemberName("unsupported")]
    Unsupported,
}

/// <summary>
/// What Motif knows about one inspector subject, section by section, each from its own source: FieldWorks' facts
/// from the Baseline, the words that use it and the words it ran in from the stored Parse all words, and the
/// findings that name it from the stored grammar check. A section that can't be read says why, and the others are
/// still read: the Baseline's facts never wait for a Parse all words.
/// </summary>
/// <param name="Subject">The subject asked about, as asked.</param>
/// <param name="Resolution">Whether the Baseline holds the subject as one consistent object.</param>
public sealed record InspectResponse(InspectorSubject Subject, InspectorResolution Resolution)
{
    /// <summary>Whether FieldWorks has been saved since the Baseline the facts were read from.</summary>
    public bool IsStale { get; init; }

    /// <summary>The stored Assessment the words and timings come from, or <see langword="null"/> when none matches.</summary>
    public string? AssessmentId { get; init; }

    /// <summary>
    /// The kind and key PanGloss times the subject under, read from the Baseline for a morpheme;
    /// <see langword="null"/> for a subject PanGloss does not time, which then has no timing section.
    /// </summary>
    public TraceTimingKey? TimingKey { get; init; }

    /// <summary>What the Baseline's copy of the FieldWorks project says about the subject.</summary>
    public InspectorSection<ObjectFacts> Facts { get; init; } =
        InspectorSection<ObjectFacts>.Not(InspectorSectionStatus.NotRequested, "Not asked for.");

    /// <summary>The Selection's words with a stored analysis, other than a disapproved one, that uses the subject.</summary>
    public InspectorSection<ObjectUseWords> Uses { get; init; } =
        InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.NotRequested, "Not asked for.");

    /// <summary>The Selection's words the subject ran in, from the stored per-word timings.</summary>
    public InspectorSection<ObjectUseWords> RanIn { get; init; } =
        InspectorSection<ObjectUseWords>.Not(InspectorSectionStatus.NotRequested, "Not asked for.");

    /// <summary>The stored grammar check's findings that name the subject or reach it, by identity.</summary>
    public InspectorSection<IReadOnlyList<GrammarWarning>> Warnings { get; init; } =
        InspectorSection<IReadOnlyList<GrammarWarning>>.Not(InspectorSectionStatus.NotRequested, "Not asked for.");
}
