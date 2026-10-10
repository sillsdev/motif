using System;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>The closed direction choices supported by a regular rewrite rule.</summary>
public enum PhonologicalRuleDirection
{
    /// <summary>Apply the rule from the left edge toward the right edge.</summary>
    LeftToRightIterative,

    /// <summary>Apply the rule from the right edge toward the left edge.</summary>
    RightToLeftIterative,

    /// <summary>Apply all matches simultaneously.</summary>
    Simultaneous,
}

/// <summary>One input or output item in a simple phonological rewrite rule.</summary>
/// <param name="Phoneme">The identity of an existing phoneme, when the item names a phoneme.</param>
/// <param name="NaturalClass">The identity of an existing natural class, when the item names a class.</param>
public sealed record PhonologicalRuleItem(CanonicalId? Phoneme = null, CanonicalId? NaturalClass = null);

/// <summary>One regular rewrite rule with typed input, output, contexts and declared rule placement.</summary>
/// <param name="Name">The rule name stored in the project's default analysis writing system.</param>
/// <param name="Direction">The closed direction value to author.</param>
/// <param name="Input">Zero or one phoneme or natural-class items; an empty list means insertion.</param>
/// <param name="Output">Zero or one phoneme or natural-class items; an empty list means deletion.</param>
/// <param name="Left">A simple context or an ordered sequence of simple contexts.</param>
/// <param name="Right">A simple context or an ordered sequence of simple contexts.</param>
/// <param name="Placement">The neighboring rule identities that define the insertion gap.</param>
public sealed record AuthorPhonologicalRuleIntent(
    string Name,
    PhonologicalRuleDirection Direction,
    IReadOnlyList<PhonologicalRuleItem> Input,
    IReadOnlyList<PhonologicalRuleItem> Output,
    IReadOnlyList<EnvironmentContext> Left,
    IReadOnlyList<EnvironmentContext> Right,
    Placement? Placement = null);

/// <summary>Parses the closed JSON input for <see cref="AuthorPhonologicalRuleIntent"/>.</summary>
public static class AuthorPhonologicalRuleIntentParser
{
    private const int MaximumSequenceMembers = 8;

    /// <summary>Parses a single closed rewrite-rule intent.</summary>
    /// <param name="authored">The JSON object authored by a caller.</param>
    /// <exception cref="ContractParseException">The JSON is malformed or requests unsupported rule notation.</exception>
    public static AuthorPhonologicalRuleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorPhonologicalRule", "name", "direction", "input",
            "output", "left", "right", "placement");
        var input = Items(authored, "input");
        var output = Items(authored, "output");
        var left = AuthorEnvironmentIntentParser.Contexts(authored, "left");
        var right = AuthorEnvironmentIntentParser.Contexts(authored, "right");
        RequireContextLength(left, "left");
        RequireContextLength(right, "right");
        return new(SoundSystemIntentParsing.Text(authored, "name"), Direction(authored), input, output,
            left, right, Placement(authored));
    }

    private static PhonologicalRuleItem[] Items(JsonElement authored, string name)
    {
        var items = SoundSystemIntentParsing.Array(authored, name).Select(item =>
        {
            SoundSystemIntentParsing.Object(item, "phonological rule item", "phoneme", "naturalClass");
            if (item.EnumerateObject().Count() != 1)
                throw new ContractParseException("Each rule input or output item must name one phoneme or natural class.");
            return new PhonologicalRuleItem(
                item.TryGetProperty("phoneme", out _) ? SoundSystemIntentParsing.Id(item, "phoneme") : null,
                item.TryGetProperty("naturalClass", out _) ? SoundSystemIntentParsing.Id(item, "naturalClass") : null);
        }).ToArray();
        if (items.Length > 1)
            throw new ContractParseException($"A simple rewrite rule supports at most one {name} item.");
        return items;
    }

    private static PhonologicalRuleDirection Direction(JsonElement authored) =>
        SoundSystemIntentParsing.Text(authored, "direction") switch
        {
            "left-to-right" => PhonologicalRuleDirection.LeftToRightIterative,
            "right-to-left" => PhonologicalRuleDirection.RightToLeftIterative,
            "simultaneous" => PhonologicalRuleDirection.Simultaneous,
            var value => throw new ContractParseException(
                $"Rule direction '{value}' must be 'left-to-right', 'right-to-left' or 'simultaneous'."),
        };

    private static Placement? Placement(JsonElement authored)
    {
        if (!authored.TryGetProperty("placement", out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        SoundSystemIntentParsing.Object(value, "rule placement", "after", "before");
        CanonicalId? after = value.TryGetProperty("after", out _) ? SoundSystemIntentParsing.Id(value, "after") : null;
        CanonicalId? before = value.TryGetProperty("before", out _) ? SoundSystemIntentParsing.Id(value, "before") : null;
        if (after is null && before is null)
            throw new ContractParseException("Rule placement must name an existing neighboring rule.");
        return new Placement(after, before);
    }

    private static void RequireContextLength(EnvironmentContext[] contexts, string side)
    {
        if (contexts.Length is 0 or > MaximumSequenceMembers)
            throw new ContractParseException(
                $"The {side} context must contain between 1 and {MaximumSequenceMembers} simple items.");
    }
}
