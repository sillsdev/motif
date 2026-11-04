using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes whether one existing inflectional affix slot is optional, renames it, or both.</summary>
/// <param name="Target">Canonical id of the existing slot.</param>
/// <param name="ExpectedOptional">The slot value that must still be present in the project; null when optionality is not edited.</param>
/// <param name="Optional">The requested optionality; null when optionality is not edited.</param>
/// <param name="Rename">The requested new name with the name it must still have; null when the slot is not renamed.</param>
public sealed record EditAffixSlotIntent(CanonicalId Target, bool? ExpectedOptional, bool? Optional,
    EditAffixSlotRename? Rename = null);

/// <summary>A guarded slot rename in one writing system.</summary>
/// <param name="WritingSystem">Tag of the writing system whose name changes.</param>
/// <param name="ExpectedName">The name the slot must still have there; empty when it has none.</param>
/// <param name="Name">The new, nonblank name.</param>
public sealed record EditAffixSlotRename(string WritingSystem, string ExpectedName, string Name);

/// <summary>Parses the closed JSON intent for changing one slot's optionality.</summary>
public static class EditAffixSlotIntentParser
{
    private const string ConstructName = "EditAffixSlot";

    /// <summary>Reads the target and explicit expected and requested values.</summary>
    public static EditAffixSlotIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "target", "expectedOptional", "optional",
            "writingSystem", "expectedName", "name");
        var hasOptionality = authored.TryGetProperty("expectedOptional", out _) || authored.TryGetProperty("optional", out _);
        var hasRename = authored.TryGetProperty("writingSystem", out _) || authored.TryGetProperty("expectedName", out _) ||
                        authored.TryGetProperty("name", out _);
        if (!hasOptionality && !hasRename)
            throw new ContractParseException(
                $"'{ConstructName}': give expectedOptional with optional, or writingSystem with expectedName and name, or both.");
        var rename = hasRename
            ? new EditAffixSlotRename(SoundSystemIntentParsing.Text(authored, "writingSystem"),
                RequiredString(authored, "expectedName"), SoundSystemIntentParsing.Text(authored, "name"))
            : null;
        return new EditAffixSlotIntent(SoundSystemIntentParsing.Id(authored, "target"),
            hasOptionality ? RequiredBoolean(authored, "expectedOptional") : null,
            hasOptionality ? RequiredBoolean(authored, "optional") : null, rename);
    }

    private static string RequiredString(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' is required and must be a string.");
        return value.GetString()!;
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
