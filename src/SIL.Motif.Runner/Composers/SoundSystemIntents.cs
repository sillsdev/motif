using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>A closed phonological feature and one of its declared symbolic values.</summary>
public sealed record PhonologicalFeatureValue(CanonicalId Feature, CanonicalId Value);

/// <summary>Authors one phoneme with explicit grapheme codes in the first vernacular writing system.</summary>
public sealed record AuthorPhonemeIntent(string Name, IReadOnlyList<string> Representations,
    IReadOnlyList<PhonologicalFeatureValue>? Features = null);

/// <summary>Authors exactly one kind of natural class; segment and feature descriptions cannot be combined.</summary>
public sealed record AuthorNaturalClassIntent(string Name, string Abbreviation,
    IReadOnlyList<CanonicalId>? Members = null, IReadOnlyList<PhonologicalFeatureValue>? Features = null);

/// <summary>One context item: exactly one phoneme, natural class, named marker, or word/morpheme boundary.</summary>
/// <param name="Phoneme">An existing phoneme.</param>
/// <param name="NaturalClass">An existing natural class.</param>
/// <param name="Boundary">The word or morpheme boundary.</param>
/// <param name="BoundaryMarker">A stored boundary marker.</param>
/// <param name="Minimum">
/// The fewest times a phoneme or natural class must repeat; null means the item appears exactly once. Only rule
/// contexts accept it.
/// </param>
/// <param name="Maximum">The most times it may repeat, or -1 for no limit; set together with <paramref name="Minimum"/>.</param>
/// <param name="Variables">Alpha references in rule natural-class contexts only.</param>
public sealed record EnvironmentContext(CanonicalId? Phoneme = null, CanonicalId? NaturalClass = null,
    string? Boundary = null, CanonicalId? BoundaryMarker = null, int? Minimum = null, int? Maximum = null,
    IReadOnlyList<PhonologicalRuleVariable>? Variables = null);

/// <summary>Authors a parser-visible environment string from typed left and right contexts.</summary>
public sealed record AuthorEnvironmentIntent(string Name, IReadOnlyList<EnvironmentContext> Left,
    IReadOnlyList<EnvironmentContext> Right);

public static class AuthorPhonemeIntentParser
{
    public static AuthorPhonemeIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorPhoneme", "name", "representations", "features");
        return new(SoundSystemIntentParsing.Text(authored, "name"),
            SoundSystemIntentParsing.Array(authored, "representations").Select(e =>
                e.ValueKind == JsonValueKind.String ? e.GetString()! : throw new ContractParseException("Representations must be strings.")).ToArray(),
            SoundSystemIntentParsing.Features(authored));
    }
}

public static class AuthorNaturalClassIntentParser
{
    public static AuthorNaturalClassIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorNaturalClass", "name", "abbreviation", "members", "features");
        return new(SoundSystemIntentParsing.Text(authored, "name"), SoundSystemIntentParsing.Text(authored, "abbreviation"),
            authored.TryGetProperty("members", out _) ? SoundSystemIntentParsing.Array(authored, "members")
                .Select(e => e.ValueKind == JsonValueKind.String && CanonicalId.TryParse(e.GetString(), out var id) ? id :
                    throw new ContractParseException("Natural class members must be canonical id strings.")).ToArray() : null,
            SoundSystemIntentParsing.Features(authored));
    }
}

public static class AuthorEnvironmentIntentParser
{
    public static AuthorEnvironmentIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorEnvironment", "name", "left", "right");
        return new(SoundSystemIntentParsing.Text(authored, "name"), Contexts(authored, "left"), Contexts(authored, "right"));
    }

    internal static EnvironmentContext[] Contexts(JsonElement authored, string name, bool allowRepeat = false) =>
        SoundSystemIntentParsing.Array(authored, name).Select(e =>
        {
            if (allowRepeat)
                SoundSystemIntentParsing.Object(e, "environment context", "phoneme", "naturalClass", "boundary",
                    "boundaryMarker", "optional", "minimum", "maximum", "variables");
            else
                SoundSystemIntentParsing.Object(e, "environment context", "phoneme", "naturalClass", "boundary", "boundaryMarker");
            var references = new[] { "phoneme", "naturalClass", "boundary", "boundaryMarker" }
                .Count(property => e.TryGetProperty(property, out _));
            if (references != 1)
                throw new ContractParseException("Each environment context must name exactly one phoneme, naturalClass, boundary or boundaryMarker.");
            var (minimum, maximum) = allowRepeat ? Repeat(e) : (null, null);
            return new EnvironmentContext(
                e.TryGetProperty("phoneme", out _) ? SoundSystemIntentParsing.Id(e, "phoneme") : null,
                e.TryGetProperty("naturalClass", out _) ? SoundSystemIntentParsing.Id(e, "naturalClass") : null,
                e.TryGetProperty("boundary", out _) ? SoundSystemIntentParsing.Text(e, "boundary") : null,
                e.TryGetProperty("boundaryMarker", out _) ? SoundSystemIntentParsing.Id(e, "boundaryMarker") : null,
                minimum, maximum, allowRepeat ? AuthorPhonologicalRuleIntentParser.ParseVariables(e) : null);
        }).ToArray();

    /// <summary>Reads optional, or minimum and maximum (-1 is unbounded).</summary>
    private static (int? Minimum, int? Maximum) Repeat(JsonElement e)
    {
        var optional = e.TryGetProperty("optional", out var flag)
            ? flag.ValueKind is JsonValueKind.True or JsonValueKind.False ? flag.GetBoolean()
                : throw new ContractParseException("'optional' must be a Boolean.")
            : false;
        var hasMinimum = e.TryGetProperty("minimum", out _);
        var hasMaximum = e.TryGetProperty("maximum", out _);
        if (optional && (hasMinimum || hasMaximum))
            throw new ContractParseException("Give 'optional' or 'minimum'/'maximum', not both.");
        if (optional) return (0, 1);
        if (!hasMinimum && !hasMaximum) return (null, null);
        var minimum = hasMinimum ? Count(e, "minimum") : 0;
        var maximum = hasMaximum ? Count(e, "maximum") : -1;
        if (minimum < 0) throw new ContractParseException("'minimum' cannot be negative.");
        if (maximum != -1 && (maximum < 1 || maximum < minimum))
            throw new ContractParseException("'maximum' must be -1 (no limit) or at least 1 and at least 'minimum'.");
        return minimum == 1 && maximum == 1 ? (null, null) : (minimum, maximum);
    }

    private static int Count(JsonElement e, string name) =>
        e.GetProperty(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var count)
            ? count
            : throw new ContractParseException($"'{name}' must be a whole number.");
}

internal static class SoundSystemIntentParsing
{
    internal static void Object(JsonElement element, string kind, params string[] fields)
    {
        ClosedPayloadParsing.RequireObject(element, kind);
        ClosedPayloadParsing.RejectUnknownProperties(element, fields, kind);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name)) throw new ContractParseException($"'{kind}': duplicate property '{property.Name}'.");
    }

    internal static string Text(JsonElement element, string name) => ClosedPayloadParsing.GetRequiredString(element, name, "intent");
    internal static CanonicalId Id(JsonElement element, string name) => ClosedPayloadParsing.GetRequiredCanonicalId(element, name, "intent");

    internal static CanonicalId[] Ids(JsonElement element, string name, bool ordered)
    {
        var values = Array(element, name).Select(value =>
            value.ValueKind == JsonValueKind.String && CanonicalId.TryParse(value.GetString(), out var id)
                ? id
                : throw new ContractParseException($"'{name}' must contain canonical id strings.")).ToArray();
        if (values.Distinct().Count() != values.Length)
            throw new ContractParseException($"'{name}' must not contain duplicate ids.");
        return ordered ? values : values.OrderBy(value => value.Value, StringComparer.Ordinal).ToArray();
    }

    internal static JsonElement[] Array(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            throw new ContractParseException($"'{name}' is required and must be an array.");
        return value.EnumerateArray().ToArray();
    }

    internal static PhonologicalFeatureValue[]? Features(JsonElement authored, string name = "features") =>
        !authored.TryGetProperty(name, out _) ? null : Array(authored, name).Select(e =>
        {
            Object(e, "feature value", "feature", "value");
            return new PhonologicalFeatureValue(Id(e, "feature"), Id(e, "value"));
        }).ToArray();
}
