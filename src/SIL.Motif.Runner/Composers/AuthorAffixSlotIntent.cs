using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names a new inflectional slot under an existing category and optionally assigns it to affixes.</summary>
/// <param name="Category">The existing category that owns the slot.</param>
/// <param name="Name">The slot name shown in category and affix membership choices.</param>
/// <param name="WritingSystem">The writing system tag for <paramref name="Name"/>.</param>
/// <param name="Optional">Whether forms may omit this slot.</param>
/// <param name="Assignments">Existing inflectional affix MSAs to assign to the new slot.</param>
public sealed record AuthorAffixSlotIntent(
    CanonicalId Category,
    string Name,
    string WritingSystem,
    bool Optional,
    IReadOnlyList<CanonicalId>? Assignments = null);

/// <summary>Parses the closed JSON intent for creating one category-owned inflectional slot.</summary>
public static class AuthorAffixSlotIntentParser
{
    private const string ConstructName = "AuthorAffixSlot";

    /// <summary>Reads category, localized name, optionality, and an optional set of assigned MSAs.</summary>
    public static AuthorAffixSlotIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "category", "name", "ws", "optional", "assignments");
        if (!authored.TryGetProperty("optional", out var optional) ||
            optional.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException($"'{ConstructName}': 'optional' is required and must be a Boolean.");

        return new AuthorAffixSlotIntent(
            SoundSystemIntentParsing.Id(authored, "category"),
            SoundSystemIntentParsing.Text(authored, "name"),
            SoundSystemIntentParsing.Text(authored, "ws"),
            optional.GetBoolean(),
            authored.TryGetProperty("assignments", out _) ?
                SoundSystemIntentParsing.Ids(authored, "assignments", ordered: false) : null);
    }
}
