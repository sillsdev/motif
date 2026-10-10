using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names one flat ad hoc prohibition and the expected and requested Disabled values.</summary>
/// <param name="Target">Canonical id of an existing allomorph or morpheme prohibition.</param>
/// <param name="ExpectedDisabled">The value the project must still hold when the Draft is composed.</param>
/// <param name="Disabled">The explicit value to write for the prohibition's Disabled field.</param>
public sealed record EditAdhocProhibitionIntent(CanonicalId Target, bool ExpectedDisabled, bool Disabled);

/// <summary>Parses the closed JSON intent for editing one flat ad hoc prohibition.</summary>
public static class EditAdhocProhibitionIntentParser
{
    private const string ConstructName = "EditAdhocProhibition";

    /// <summary>Reads the target and both required Boolean values; any other property is refused.</summary>
    public static EditAdhocProhibitionIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "target", "expectedDisabled", "disabled");
        return new EditAdhocProhibitionIntent(
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
