using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One captured human Opinion and its source analysis identity.</summary>
public sealed record HumanAnalysisDecision(string AnalysisId, string Opinion, ApprovedMorphology Morphology);

/// <summary>The spelling decision captured from the FieldWorks wordform.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<CapturedSpellingStatus>))]
public enum CapturedSpellingStatus
{
    Undecided,
    Correct,
    Incorrect,
    Unavailable
}

/// <summary>How much of the parser result and captured decisions can support a classification.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementEvidenceAvailability>))]
public enum HumanAgreementEvidenceAvailability
{
    [JsonStringEnumMemberName("available")]
    Available,
    [JsonStringEnumMemberName("partially-available")]
    PartiallyAvailable,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
    [JsonStringEnumMemberName("not-measured")]
    NotMeasured
}

/// <summary>Whether the parser completed the search needed to establish absences.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSearchCompletion>))]
public enum HumanAgreementSearchCompletion
{
    [JsonStringEnumMemberName("complete")]
    Complete,
    [JsonStringEnumMemberName("incomplete")]
    Incomplete,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
    [JsonStringEnumMemberName("not-measured")]
    NotMeasured
}

/// <summary>A named reason a classification cannot establish every result.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementLimitation>))]
public enum HumanAgreementLimitation
{
    [JsonStringEnumMemberName("capped")]
    Capped,
    [JsonStringEnumMemberName("timed-out")]
    TimedOut,
    [JsonStringEnumMemberName("projection-unavailable")]
    ProjectionUnavailable,
    [JsonStringEnumMemberName("parser-refused")]
    ParserRefused,
    [JsonStringEnumMemberName("invalid-shape")]
    InvalidShape,
    [JsonStringEnumMemberName("malformed-evidence")]
    MalformedEvidence,
    [JsonStringEnumMemberName("invalid-expected-morphology")]
    InvalidExpectedMorphology
}

/// <summary>The Approved reading result established by the available evidence.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementApprovalState>))]
public enum HumanAgreementApprovalState
{
    [JsonStringEnumMemberName("no-parse")]
    NoParse,
    [JsonStringEnumMemberName("no-approved-match")]
    NoApprovedMatch,
    [JsonStringEnumMemberName("approved-with-alternatives")]
    ApprovedWithAlternatives,
    [JsonStringEnumMemberName("approved-unique")]
    ApprovedUnique,
    [JsonStringEnumMemberName("no-approved-expectations")]
    NoApprovedExpectations,
    [JsonStringEnumMemberName("incomplete")]
    Incomplete,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable,
    [JsonStringEnumMemberName("not-measured")]
    NotMeasured,
    [JsonStringEnumMemberName("conflicting-decisions")]
    ConflictingDecisions
}

/// <summary>The spelling result independent of analysis Opinion.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSpellingAcceptance>))]
public enum HumanAgreementSpellingAcceptance
{
    [JsonStringEnumMemberName("accepted-misspelling")]
    AcceptedMisspelling,
    [JsonStringEnumMemberName("rejected-misspelling")]
    RejectedMisspelling,
    [JsonStringEnumMemberName("incomplete")]
    Incomplete,
    [JsonStringEnumMemberName("not-applicable")]
    NotApplicable,
    [JsonStringEnumMemberName("unavailable")]
    Unavailable
}

/// <summary>The human Opinion attached to a matched source analysis.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementOpinion>))]
public enum HumanAgreementOpinion
{
    [JsonStringEnumMemberName("approved")]
    Approved,
    [JsonStringEnumMemberName("disapproved")]
    Disapproved,
    [JsonStringEnumMemberName("unknown")]
    Unknown
}

/// <summary>A produced morphology identity that did not match any captured human decision.</summary>
public sealed record HumanAgreementProducedIdentity(IReadOnlyList<HumanAgreementProducedMorph> Morphs);

/// <summary>One ordered morph in a produced parser identity.</summary>
public sealed record HumanAgreementProducedMorph(string Form, string Msa, string? InflType, string? GuessedStringNfd);

/// <summary>The kind of disagreement retained in the classification.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementConflictKind>))]
public enum HumanAgreementConflictKind
{
    [JsonStringEnumMemberName("opposed-opinions")]
    OpposedOpinions,
    [JsonStringEnumMemberName("approved-with-incorrect-spelling")]
    ApprovedWithIncorrectSpelling
}

/// <summary>A disagreement and every source analysis that witnesses it.</summary>
public sealed record HumanAgreementConflict(
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementConflictKind>))]
    HumanAgreementConflictKind Kind,
    IReadOnlyList<string> AnalysisIds);

/// <summary>A negative observation made by a produced reading.</summary>
[JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementNegativeEvidenceKind>))]
public enum HumanAgreementNegativeEvidenceKind
{
    [JsonStringEnumMemberName("disapproved-parse")]
    DisapprovedParse,
    [JsonStringEnumMemberName("misspelled-parse")]
    MisspelledParse
}

/// <summary>A negative observation and its matched source witnesses.</summary>
public sealed record HumanAgreementNegativeEvidence(
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementNegativeEvidenceKind>))]
    HumanAgreementNegativeEvidenceKind Kind,
    IReadOnlyList<string> AnalysisIds);

/// <summary>The immutable classification of one parser result against captured human decisions.</summary>
public sealed record HumanAgreementFacts(
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementEvidenceAvailability>))]
    HumanAgreementEvidenceAvailability Availability,
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSearchCompletion>))]
    HumanAgreementSearchCompletion Completion,
    [property: JsonConverter(typeof(StrictStringEnumListConverter<HumanAgreementLimitation>))]
    IReadOnlyList<HumanAgreementLimitation> Limitations,
    [property: JsonConverter(typeof(StrictNullableStringEnumConverter<HumanAgreementApprovalState>))]
    HumanAgreementApprovalState? ApprovedState,
    int ProducedIdentityCount,
    int ApprovedExpectedMorphologyCount,
    int MatchedApprovedMorphologyCount,
    IReadOnlyList<string> ObservedUnmatchedExpectedAnalysisIds,
    IReadOnlyList<string> ProvenMissingApprovedAnalysisIds,
    IReadOnlyList<string> MatchedApprovedAnalysisIds,
    IReadOnlyList<string> MatchedDisapprovedAnalysisIds,
    IReadOnlyList<string> MatchedUnknownAnalysisIds,
    IReadOnlyList<HumanAgreementProducedIdentity> UnmatchedProducedIdentities,
    IReadOnlyList<HumanAgreementConflict> Conflicts,
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSpellingAcceptance>))]
    HumanAgreementSpellingAcceptance SpellingAcceptance,
    IReadOnlyList<HumanAgreementNegativeEvidence> NegativeEvidence);

/// <summary>Reads and writes only declared JSON string names for one enum.</summary>
public sealed class StrictStringEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly IReadOnlyDictionary<TEnum, string> Names = BuildNames();
    private static readonly IReadOnlyDictionary<string, TEnum> Values = Names.ToDictionary(
        pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } name && Values.TryGetValue(name, out var value))
            return value;

        throw new JsonException($"Expected a declared string name for {typeof(TEnum).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!Names.TryGetValue(value, out var name))
            throw new JsonException($"Cannot write an undeclared {typeof(TEnum).Name} value.");
        writer.WriteStringValue(name);
    }

    private static IReadOnlyDictionary<TEnum, string> BuildNames()
    {
        var names = new Dictionary<TEnum, string>();
        foreach (var value in Enum.GetValues<TEnum>())
        {
            var member = typeof(TEnum).GetField(Enum.GetName(value)!)!;
            var name = member.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? member.Name;
            names.Add(value, name);
        }
        return names;
    }
}

/// <summary>Reads and writes a nullable enum using only declared JSON string names.</summary>
public sealed class StrictNullableStringEnumConverter<TEnum> : JsonConverter<TEnum?> where TEnum : struct, Enum
{
    private static readonly StrictStringEnumConverter<TEnum> ValueConverter = new();

    public override bool HandleNull => true;

    public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? null : ValueConverter.Read(ref reader, typeof(TEnum), options);

    public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else ValueConverter.Write(writer, value.Value, options);
    }
}

/// <summary>Reads and writes enum arrays using only declared JSON string names.</summary>
public sealed class StrictStringEnumListConverter<TEnum> : JsonConverter<IReadOnlyList<TEnum>> where TEnum : struct, Enum
{
    private static readonly StrictStringEnumConverter<TEnum> ValueConverter = new();

    public override bool HandleNull => true;

    public override IReadOnlyList<TEnum> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected an array of enum names.");
        var values = new List<TEnum>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) return Array.AsReadOnly(values.ToArray());
            values.Add(ValueConverter.Read(ref reader, typeof(TEnum), options));
        }
        throw new JsonException("The enum-name array ended before its closing token.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<TEnum> values, JsonSerializerOptions options)
    {
        if (values is null) throw new JsonException("Expected an array of enum names.");
        writer.WriteStartArray();
        foreach (var value in values) ValueConverter.Write(writer, value, options);
        writer.WriteEndArray();
    }
}
