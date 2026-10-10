using System.Collections.Generic;
using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Changes the slots assigned to one existing inflectional affix MSA.</summary>
/// <param name="Target">Canonical id of the existing inflectional MSA.</param>
/// <param name="ExpectedSlots">The slot memberships that must still be present in the project.</param>
/// <param name="Slots">The requested slot memberships, in canonical identity order.</param>
public sealed record EditInflectionalAffixIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedSlots, IReadOnlyList<CanonicalId> Slots);

/// <summary>Parses the closed JSON intent for changing an inflectional affix's slot membership.</summary>
public static class EditInflectionalAffixIntentParser
{
    /// <summary>Reads the target and slot identities, rejecting duplicates and sorting the unordered set.</summary>
    public static EditInflectionalAffixIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "EditInflectionalAffix", "target", "expectedSlots", "slots");
        return new EditInflectionalAffixIntent(SoundSystemIntentParsing.Id(authored, "target"),
            SoundSystemIntentParsing.Ids(authored, "expectedSlots", ordered: false),
            SoundSystemIntentParsing.Ids(authored, "slots", ordered: false));
    }
}
