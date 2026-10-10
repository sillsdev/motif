using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Names one category-owned template and its ordered prefix and suffix slots.</summary>
/// <param name="Category">The existing category that owns the template.</param>
/// <param name="Name">The template name shown in the category's grammar.</param>
/// <param name="WritingSystem">The writing-system tag for <paramref name="Name"/>.</param>
/// <param name="PrefixSlots">Slots in the template's authored prefix order.</param>
/// <param name="SuffixSlots">Slots in the template's authored suffix order.</param>
/// <param name="Final">Whether this template is marked as requiring further derivation.</param>
public sealed record AuthorAffixTemplateIntent(
    CanonicalId Category,
    string Name,
    string WritingSystem,
    IReadOnlyList<CanonicalId> PrefixSlots,
    IReadOnlyList<CanonicalId> SuffixSlots,
    bool Final);

/// <summary>Parses the closed JSON intent for creating one alternative affix template.</summary>
public static class AuthorAffixTemplateIntentParser
{
    private const string ConstructName = "AuthorAffixTemplate";

    /// <summary>Reads category, localized name, ordered slot lists, and finality.</summary>
    public static AuthorAffixTemplateIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName,
            "category", "name", "ws", "prefixSlots", "suffixSlots", "final");
        if (!authored.TryGetProperty("final", out var final) ||
            final.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new ContractParseException($"'{ConstructName}': 'final' is required and must be a Boolean.");

        return new AuthorAffixTemplateIntent(
            SoundSystemIntentParsing.Id(authored, "category"),
            SoundSystemIntentParsing.Text(authored, "name"),
            SoundSystemIntentParsing.Text(authored, "ws"),
            SoundSystemIntentParsing.Ids(authored, "prefixSlots", ordered: true),
            SoundSystemIntentParsing.Ids(authored, "suffixSlots", ordered: true),
            final.GetBoolean());
    }
}
