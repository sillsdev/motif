using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names one compound rule and the expected and requested Disabled values.</summary>
/// <param name="Target">Canonical id of an existing endocentric or exocentric compound rule.</param>
/// <param name="ExpectedDisabled">The value the project must still hold when the Draft is composed.</param>
/// <param name="Disabled">The explicit value to write for the rule's Disabled field.</param>
public sealed record EditCompoundRuleIntent(CanonicalId Target, bool ExpectedDisabled, bool Disabled);

/// <summary>Parses the closed JSON intent for turning one compound rule off or on.</summary>
public static class EditCompoundRuleIntentParser
{
    private const string ConstructName = "EditCompoundRule";

    /// <summary>Reads the target and both required Boolean values; any other property is refused.</summary>
    public static EditCompoundRuleIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "target", "expectedDisabled", "disabled");
        return new EditCompoundRuleIntent(
            SoundSystemIntentParsing.Id(authored, "target"),
            RequiredBoolean(authored, "expectedDisabled"),
            RequiredBoolean(authored, "disabled"));
    }

    private static bool RequiredBoolean(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException(
                $"'{ConstructName}': '{propertyName}' is required and must be a Boolean.");
        return value.GetBoolean();
    }
}
