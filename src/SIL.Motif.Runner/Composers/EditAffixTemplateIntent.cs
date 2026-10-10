using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Reorders the existing prefix and suffix slots of one affix template.</summary>
/// <param name="Target">Canonical id of the existing template.</param>
/// <param name="ExpectedPrefixSlots">The ordered prefix slots that must still be present in the project.</param>
/// <param name="PrefixSlots">The requested ordered prefix slots.</param>
/// <param name="ExpectedSuffixSlots">The ordered suffix slots that must still be present in the project.</param>
/// <param name="SuffixSlots">The requested ordered suffix slots.</param>
public sealed record EditAffixTemplateIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedPrefixSlots, IReadOnlyList<CanonicalId> PrefixSlots,
    IReadOnlyList<CanonicalId> ExpectedSuffixSlots, IReadOnlyList<CanonicalId> SuffixSlots);

/// <summary>Parses the closed JSON intent for reordering an existing template's slots.</summary>
public static class EditAffixTemplateIntentParser
{
    /// <summary>Reads both expected sequences and both requested sequences in their authored order.</summary>
    public static EditAffixTemplateIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditAffixTemplate", "target", "expectedPrefixSlots", "prefixSlots",
            "expectedSuffixSlots", "suffixSlots");
        return new EditAffixTemplateIntent(SoundSystemIntentParsing.Id(authored, "target"),
            SoundSystemIntentParsing.Ids(authored, "expectedPrefixSlots", ordered: true),
            SoundSystemIntentParsing.Ids(authored, "prefixSlots", ordered: true),
            SoundSystemIntentParsing.Ids(authored, "expectedSuffixSlots", ordered: true),
            SoundSystemIntentParsing.Ids(authored, "suffixSlots", ordered: true));
    }
}
