using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes whether one existing inflectional affix slot is optional.</summary>
/// <param name="Target">Canonical id of the existing slot.</param>
/// <param name="ExpectedOptional">The slot value that must still be present in the project.</param>
/// <param name="Optional">The requested optionality.</param>
public sealed record EditAffixSlotIntent(CanonicalId Target, bool ExpectedOptional, bool Optional);

/// <summary>Parses the closed JSON intent for changing one slot's optionality.</summary>
public static class EditAffixSlotIntentParser
{
    private const string ConstructName = "EditAffixSlot";

    /// <summary>Reads the target and explicit expected and requested values.</summary>
    public static EditAffixSlotIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "target", "expectedOptional", "optional");
        return new EditAffixSlotIntent(SoundSystemIntentParsing.Id(authored, "target"),
            RequiredBoolean(authored, "expectedOptional"), RequiredBoolean(authored, "optional"));
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
