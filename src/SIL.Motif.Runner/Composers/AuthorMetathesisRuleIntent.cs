using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// One metathesis rule: the sounds in the left and right switch positions trade places. The environments, the switch
/// positions and the optional middle context are the parts of a FieldWorks metathesis rule's formula.
/// </summary>
/// <param name="Name">The rule name stored in the project's default analysis writing system.</param>
/// <param name="Direction">The closed direction value to author.</param>
/// <param name="Left">Up to eight contexts before the left switch; phoneme, natural class or boundary.</param>
/// <param name="LeftSwitch">The phoneme or natural class that moves to the right switch position.</param>
/// <param name="Middle">The optional context between the switches, which stays where it is.</param>
/// <param name="RightSwitch">The phoneme or natural class that moves to the left switch position.</param>
/// <param name="Right">Up to eight contexts after the right switch; phoneme, natural class or boundary.</param>
/// <param name="Placement">The neighboring rule identities that define the insertion gap.</param>
/// <param name="MiddleWithLeft">Whether the middle context belongs to the left switch rather than the right one.</param>
public sealed record AuthorMetathesisRuleIntent(
    string Name,
    PhonologicalRuleDirection Direction,
    IReadOnlyList<EnvironmentContext> Left,
    PhonologicalRuleItem LeftSwitch,
    PhonologicalRuleItem? Middle,
    PhonologicalRuleItem RightSwitch,
    IReadOnlyList<EnvironmentContext> Right,
    Placement? Placement = null,
    bool MiddleWithLeft = true);

/// <summary>Parses the closed JSON input for <see cref="AuthorMetathesisRuleIntent"/>.</summary>
public static class AuthorMetathesisRuleIntentParser
{
    private const int MaximumEnvironmentItems = 8;

    /// <summary>Parses a single closed metathesis-rule intent.</summary>
    /// <param name="authored">The JSON object authored by a caller.</param>
    /// <exception cref="ContractParseException">The JSON is malformed or requests unsupported rule notation.</exception>
    public static AuthorMetathesisRuleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorMetathesisRule", "name", "direction", "left", "leftSwitch",
            "middle", "middleWith", "rightSwitch", "right", "placement");
        var middle = authored.TryGetProperty("middle", out _) ? Switch(authored, "middle") : null;
        var middleWithLeft = true;
        if (authored.TryGetProperty("middleWith", out _))
        {
            if (middle is null)
                throw new ContractParseException("'middleWith' needs a 'middle' context to belong to.");
            middleWithLeft = SoundSystemIntentParsing.Text(authored, "middleWith") switch
            {
                "left" => true,
                "right" => false,
                var value => throw new ContractParseException($"'middleWith' must be 'left' or 'right', not '{value}'."),
            };
        }
        return new(SoundSystemIntentParsing.Text(authored, "name"),
            AuthorPhonologicalRuleIntentParser.ParseDirection(authored), Environment(authored, "left"),
            Switch(authored, "leftSwitch") ?? throw new ContractParseException("A metathesis rule needs a 'leftSwitch'."),
            middle,
            Switch(authored, "rightSwitch") ?? throw new ContractParseException("A metathesis rule needs a 'rightSwitch'."),
            Environment(authored, "right"), AuthorPhonologicalRuleIntentParser.ParsePlacement(authored), middleWithLeft);
    }

    private static PhonologicalRuleItem? Switch(JsonElement authored, string name)
    {
        if (!authored.TryGetProperty(name, out var value)) return null;
        SoundSystemIntentParsing.Object(value, name, "phoneme", "naturalClass");
        var phoneme = value.TryGetProperty("phoneme", out _);
        var naturalClass = value.TryGetProperty("naturalClass", out _);
        if (phoneme == naturalClass)
            throw new ContractParseException($"'{name}' must name one phoneme or one natural class.");
        return phoneme
            ? new(Phoneme: SoundSystemIntentParsing.Id(value, "phoneme"))
            : new(NaturalClass: SoundSystemIntentParsing.Id(value, "naturalClass"));
    }

    private static EnvironmentContext[] Environment(JsonElement authored, string name)
    {
        if (!authored.TryGetProperty(name, out _)) return [];
        var contexts = AuthorEnvironmentIntentParser.Contexts(authored, name);
        if (contexts.Length > MaximumEnvironmentItems)
            throw new ContractParseException(
                $"The {name} environment must contain at most {MaximumEnvironmentItems} simple items.");
        return contexts;
    }
}
