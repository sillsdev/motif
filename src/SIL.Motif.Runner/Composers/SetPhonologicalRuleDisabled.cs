using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Turns one phonological rule off or on. A disabled rule is not loaded by the parser.</summary>
/// <param name="Target">Canonical id of an existing phonological rule.</param>
/// <param name="ExpectedDisabled">The value the rule must still hold when the Draft is composed.</param>
/// <param name="Disabled">The value to write.</param>
public sealed record SetPhonologicalRuleDisabledIntent(CanonicalId Target, bool ExpectedDisabled, bool Disabled);

/// <summary>Parses the closed JSON intent for disabling or enabling a rule.</summary>
public static class SetPhonologicalRuleDisabledIntentParser
{
    private const string ConstructName = "SetPhonologicalRuleDisabled";

    /// <summary>Reads the target and both Boolean values.</summary>
    public static SetPhonologicalRuleDisabledIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "target", "expectedDisabled", "disabled");
        return new(SoundSystemIntentParsing.Id(authored, "target"), Flag(authored, "expectedDisabled"), Flag(authored, "disabled"));
    }

    private static bool Flag(JsonElement authored, string name) =>
        authored.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new ContractParseException($"'{ConstructName}': '{name}' is required and must be a Boolean.");
}

/// <summary>Builds the single Disabled write, after checking the rule still holds the expected value.</summary>
public static class SetPhonologicalRuleDisabledComposer
{
    private const string ConstructName = "SetPhonologicalRuleDisabled";

    /// <summary>Returns the write, or throws on a target that is not a rule or a stale expectation.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, SetPhonologicalRuleDisabledIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var rule = LexicalFormAuthoring.Resolve<IPhSegmentRule>(cache, intent.Target, ConstructName);
        if (rule.Disabled != intent.ExpectedDisabled)
            throw new InvalidOperationException(
                $"'{ConstructName}': '{intent.Target.Value}' has Disabled={rule.Disabled}; the intent expected Disabled={intent.ExpectedDisabled}.");
        return [new OperationEnvelope(CanonicalId.Mint(), PhSegmentRuleDisabledOperationKinds.SetDisabled,
            target: intent.Target, after: JsonSerializer.SerializeToElement(new { value = intent.Disabled }),
            rationale: "Authored by the SetPhonologicalRuleDisabled composer.")];
    }
}
