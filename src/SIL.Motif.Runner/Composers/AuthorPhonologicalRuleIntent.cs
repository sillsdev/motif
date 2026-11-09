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
/// <param name="Change">Feature values to change on the matched segment; only Output accepts this item kind.</param>
/// <param name="Variables">Alpha references on a natural class or feature change.</param>
public sealed record PhonologicalRuleItem(CanonicalId? Phoneme = null, CanonicalId? NaturalClass = null,
    IReadOnlyList<PhonologicalFeatureValue>? Change = null, IReadOnlyList<PhonologicalRuleVariable>? Variables = null);

/// <summary>An alpha variable shares one closed feature across contexts; minus polarity requires disagreement.</summary>
public sealed record PhonologicalRuleVariable(string Variable, CanonicalId Feature, string Polarity = "+");

/// <summary>One regular rewrite rule with typed input, output, contexts and declared rule placement.</summary>
/// <param name="Name">The rule name stored in the project's default analysis writing system.</param>
/// <param name="Direction">The closed direction value to author.</param>
/// <param name="Input">Up to eight phoneme or natural-class items in order; an empty list means insertion.</param>
/// <param name="Output">Up to eight phoneme, natural-class or feature-change items; an empty list means deletion.</param>
/// <param name="Left">Contexts in order; an item may be optional or repeated.</param>
/// <param name="Right">Contexts in order; an item may be optional or repeated.</param>
/// <param name="Placement">The neighboring rule identities that define the insertion gap.</param>
/// <param name="RightHandSides">One or more complete ordered cases, instead of the single-outcome shorthand.</param>
public sealed record AuthorPhonologicalRuleIntent(
    string Name,
    PhonologicalRuleDirection Direction,
    IReadOnlyList<PhonologicalRuleItem> Input,
    IReadOnlyList<PhonologicalRuleItem> Output,
    IReadOnlyList<EnvironmentContext> Left,
    IReadOnlyList<EnvironmentContext> Right,
    Placement? Placement = null,
    IReadOnlyList<PhonologicalRuleOutcome>? RightHandSides = null)
{
    /// <summary>The ordered outcomes, including the single-outcome shorthand when no sequence is supplied.</summary>
    public IReadOnlyList<PhonologicalRuleOutcome> Outcomes => RightHandSides ?? [new(Output, Left, Right)];
}

/// <summary>One right-hand side with its own Output, environments and lexical applicability sets.</summary>
public sealed record PhonologicalRuleOutcome(
    IReadOnlyList<PhonologicalRuleItem> Output,
    IReadOnlyList<EnvironmentContext> Left,
    IReadOnlyList<EnvironmentContext> Right,
    IReadOnlyList<CanonicalId>? RequiredFeatures = null,
    IReadOnlyList<CanonicalId>? ExcludedFeatures = null,
    IReadOnlyList<CanonicalId>? InputCategories = null);

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
            "output", "left", "right", "placement", "rightHandSides");
        var input = Items(authored, "input");
        if (authored.TryGetProperty("rightHandSides", out _))
        {
            if (new[] { "output", "left", "right" }.Any(p => authored.TryGetProperty(p, out _)))
                throw new ContractParseException("Use rightHandSides or the single-outcome output/left/right shorthand, not both.");
            return new(SoundSystemIntentParsing.Text(authored, "name"), Direction(authored), input, [], [], [],
                Placement(authored), ParseOutcomes(authored));
        }
        var output = Items(authored, "output");
        var left = OptionalContexts(authored, "left");
        var right = OptionalContexts(authored, "right");
        RequireContextLength(left, "left");
        RequireContextLength(right, "right");
        return new(SoundSystemIntentParsing.Text(authored, "name"), Direction(authored), input, output,
            left, right, Placement(authored));
    }

    internal static PhonologicalRuleOutcome[] ParseOutcomes(JsonElement authored)
    {
        var outcomes = SoundSystemIntentParsing.Array(authored, "rightHandSides").Select(value =>
        {
            SoundSystemIntentParsing.Object(value, "right-hand side", "output", "left", "right", "requiredFeatures", "excludedFeatures", "inputCategories");
            return new PhonologicalRuleOutcome(Items(value, "output"), ParseOptionalContexts(value, "left"),
                ParseOptionalContexts(value, "right"),
                value.TryGetProperty("requiredFeatures", out _) ? SoundSystemIntentParsing.Ids(value, "requiredFeatures", ordered: false) : [],
                value.TryGetProperty("excludedFeatures", out _) ? SoundSystemIntentParsing.Ids(value, "excludedFeatures", ordered: false) : [],
                value.TryGetProperty("inputCategories", out _) ? SoundSystemIntentParsing.Ids(value, "inputCategories", ordered: false) : []);
        }).ToArray();
        if (outcomes.Length < 1)
            throw new ContractParseException("A rewrite rule needs at least one rightHandSide.");
        return outcomes;
    }

    private static EnvironmentContext[] ParseOptionalContexts(JsonElement value, string name)
    {
        var contexts = OptionalContexts(value, name);
        RequireContextLength(contexts, name);
        return contexts;
    }

    private static PhonologicalRuleItem[] Items(JsonElement authored, string name)
    {
        var items = SoundSystemIntentParsing.Array(authored, name).Select(item =>
        {
            SoundSystemIntentParsing.Object(item, "phonological rule item", "phoneme", "naturalClass", "change", "variables");
            if (new[] { "phoneme", "naturalClass", "change" }.Count(p => item.TryGetProperty(p, out _)) != 1)
                throw new ContractParseException("Each rule input or output item must name one phoneme, natural class or feature change.");
            return new PhonologicalRuleItem(
                item.TryGetProperty("phoneme", out _) ? SoundSystemIntentParsing.Id(item, "phoneme") : null,
                item.TryGetProperty("naturalClass", out _) ? SoundSystemIntentParsing.Id(item, "naturalClass") : null,
                SoundSystemIntentParsing.Features(item, "change"), ParseVariables(item));
        }).ToArray();
        if (name == "input" && items.Any(item => item.Change is not null))
            throw new ContractParseException("A feature change is allowed only in rule output.");
        if (items.Length > MaximumSequenceMembers)
            throw new ContractParseException($"A rewrite rule supports at most {MaximumSequenceMembers} {name} items.");
        return items;
    }

    internal static PhonologicalRuleVariable[]? ParseVariables(JsonElement item) =>
        !item.TryGetProperty("variables", out _) ? null : SoundSystemIntentParsing.Array(item, "variables").Select(v =>
        {
            SoundSystemIntentParsing.Object(v, "alpha reference", "variable", "feature", "polarity");
            var polarity = v.TryGetProperty("polarity", out _) ? SoundSystemIntentParsing.Text(v, "polarity") : "+";
            var variable = SoundSystemIntentParsing.Text(v, "variable");
            if (string.IsNullOrWhiteSpace(variable) || polarity is not ("+" or "-"))
                throw new ContractParseException($"Variable '{variable}' needs a name and polarity '+' or '-'.");
            return new PhonologicalRuleVariable(variable, SoundSystemIntentParsing.Id(v, "feature"), polarity);
        }).ToArray();

    internal static PhonologicalRuleDirection ParseDirection(JsonElement authored) => Direction(authored);

    internal static Placement? ParsePlacement(JsonElement authored) => Placement(authored);

    internal static PhonologicalRuleItem[] ParseItems(JsonElement authored, string name) => Items(authored, name);

    internal static EnvironmentContext[] ParseContexts(JsonElement authored, string name)
    {
        var contexts = AuthorEnvironmentIntentParser.Contexts(authored, name, allowRepeat: true);
        RequireContextLength(contexts, name);
        return contexts;
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
        if (contexts.Length > MaximumSequenceMembers)
            throw new ContractParseException(
                $"The {side} context must contain at most {MaximumSequenceMembers} simple items.");
    }

    /// <summary>A rule's left or right context may be absent, as LibLCM and FieldWorks' loader allow.</summary>
    private static EnvironmentContext[] OptionalContexts(JsonElement authored, string name) =>
        authored.TryGetProperty(name, out _) ? AuthorEnvironmentIntentParser.Contexts(authored, name, allowRepeat: true) : [];
}
