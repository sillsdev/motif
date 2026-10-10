using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a parser-visible environment and its expected typed contexts before relinking.</summary>
public sealed record EnvironmentNaturalClassRelink(CanonicalId Target,
    IReadOnlyList<EnvironmentContext> ExpectedLeft, IReadOnlyList<EnvironmentContext> ExpectedRight);

/// <summary>Moves only declared environment and rewrite-rule context users to a class created in the Draft.</summary>
public sealed record RelinkNaturalClassIntent(CanonicalId Source, CanonicalId Replacement,
    CanonicalId ReplacementCreationOperation, IReadOnlyList<EnvironmentNaturalClassRelink> Environments,
    IReadOnlyList<CanonicalId> RuleContexts);

/// <summary>Parses the typed, explicitly scoped references for a natural-class relink.</summary>
public static class RelinkNaturalClassIntentParser
{
    /// <summary>Parses a closed relink request with compare-and-set environment contexts.</summary>
    /// <param name="authored">The JSON object authored by the caller.</param>
    /// <exception cref="ContractParseException">The request contains unsupported fields or malformed identities.</exception>
    public static RelinkNaturalClassIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RelinkNaturalClass", "source", "replacement",
            "replacementCreationOperation", "environments", "ruleContexts");
        return new(SoundSystemIntentParsing.Id(authored, "source"),
            SoundSystemIntentParsing.Id(authored, "replacement"),
            SoundSystemIntentParsing.Id(authored, "replacementCreationOperation"),
            Environments(authored), Ids(authored, "ruleContexts"));
    }

    private static EnvironmentNaturalClassRelink[] Environments(JsonElement authored) =>
        SoundSystemIntentParsing.Array(authored, "environments").Select(element =>
        {
            SoundSystemIntentParsing.Object(element, "environment relink", "target", "expectedLeft", "expectedRight");
            return new EnvironmentNaturalClassRelink(SoundSystemIntentParsing.Id(element, "target"),
                AuthorEnvironmentIntentParser.Contexts(element, "expectedLeft"),
                AuthorEnvironmentIntentParser.Contexts(element, "expectedRight"));
        }).ToArray();

    private static CanonicalId[] Ids(JsonElement authored, string name)
    {
        var values = SoundSystemIntentParsing.Array(authored, name).Select(element =>
            element.ValueKind == JsonValueKind.String && CanonicalId.TryParse(element.GetString(), out var id)
                ? id
                : throw new ContractParseException($"'{name}' must contain canonical id strings.")).ToArray();
        if (values.Distinct().Count() != values.Length)
            throw new ContractParseException($"'{name}' must not contain duplicate ids.");
        return values;
    }
}
