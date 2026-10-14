using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>One captured human Opinion and its source analysis identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAnalysisDecision(
    [property: JsonRequired] string AnalysisId,
    [property: JsonRequired] string Opinion,
    [property: JsonRequired] ApprovedMorphology Morphology) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAnalysisDecision), (nameof(AnalysisId), AnalysisId),
            (nameof(Opinion), Opinion), (nameof(Morphology), Morphology));
}

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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAgreementProducedIdentity(
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedMorph> Morphs) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAgreementProducedIdentity), (nameof(Morphs), Morphs));
}

/// <summary>One ordered morph in a produced parser identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAgreementProducedMorph(
    [property: JsonRequired] string Form,
    [property: JsonRequired] string Msa,
    [property: JsonRequired] string? InflType,
    [property: JsonRequired] string? GuessedStringNfd) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAgreementProducedMorph), (nameof(Form), Form), (nameof(Msa), Msa));
}

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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAgreementConflict(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementConflictKind>))]
    HumanAgreementConflictKind Kind,
    [property: JsonRequired] IReadOnlyList<string> AnalysisIds) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAgreementConflict), (nameof(AnalysisIds), AnalysisIds));
}

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
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAgreementNegativeEvidence(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementNegativeEvidenceKind>))]
    HumanAgreementNegativeEvidenceKind Kind,
    [property: JsonRequired] IReadOnlyList<string> AnalysisIds) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAgreementNegativeEvidence), (nameof(AnalysisIds), AnalysisIds));
}

/// <summary>The immutable classification of one parser result against captured human decisions.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record HumanAgreementFacts(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementEvidenceAvailability>))]
    HumanAgreementEvidenceAvailability Availability,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSearchCompletion>))]
    HumanAgreementSearchCompletion Completion,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumListConverter<HumanAgreementLimitation>))]
    IReadOnlyList<HumanAgreementLimitation> Limitations,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictNullableStringEnumConverter<HumanAgreementApprovalState>))]
    HumanAgreementApprovalState? ApprovedState,
    [property: JsonRequired] int ProducedIdentityCount,
    [property: JsonRequired] int ApprovedExpectedMorphologyCount,
    [property: JsonRequired] int MatchedApprovedMorphologyCount,
    [property: JsonRequired] IReadOnlyList<string> ObservedUnmatchedExpectedAnalysisIds,
    [property: JsonRequired] IReadOnlyList<string> ProvenMissingApprovedAnalysisIds,
    [property: JsonRequired] IReadOnlyList<string> MatchedApprovedAnalysisIds,
    [property: JsonRequired] IReadOnlyList<string> MatchedDisapprovedAnalysisIds,
    [property: JsonRequired] IReadOnlyList<string> MatchedUnknownAnalysisIds,
    [property: JsonRequired] IReadOnlyList<HumanAgreementProducedIdentity> UnmatchedProducedIdentities,
    [property: JsonRequired] IReadOnlyList<HumanAgreementConflict> Conflicts,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementSpellingAcceptance>))]
    HumanAgreementSpellingAcceptance SpellingAcceptance,
    [property: JsonRequired] IReadOnlyList<HumanAgreementNegativeEvidence> NegativeEvidence) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(HumanAgreementFacts), (nameof(Limitations), Limitations),
            (nameof(ObservedUnmatchedExpectedAnalysisIds), ObservedUnmatchedExpectedAnalysisIds),
            (nameof(ProvenMissingApprovedAnalysisIds), ProvenMissingApprovedAnalysisIds),
            (nameof(MatchedApprovedAnalysisIds), MatchedApprovedAnalysisIds),
            (nameof(MatchedDisapprovedAnalysisIds), MatchedDisapprovedAnalysisIds),
            (nameof(MatchedUnknownAnalysisIds), MatchedUnknownAnalysisIds),
            (nameof(UnmatchedProducedIdentities), UnmatchedProducedIdentities),
            (nameof(Conflicts), Conflicts), (nameof(NegativeEvidence), NegativeEvidence));
}

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

/// <summary>Throws when a required member of a deserialized record is null.</summary>
internal static class ClosedShape
{
    public static void RequireNonNull(string record, params (string Name, object? Value)[] members)
    {
        foreach (var (name, value) in members)
            if (value is null) throw new JsonException($"{record}.{name} must not be null.");
    }
}
