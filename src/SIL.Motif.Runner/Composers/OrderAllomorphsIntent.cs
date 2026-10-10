using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Reorders all existing alternate forms on one lexical entry.</summary>
/// <param name="Target">Canonical identity of the owning lexical entry.</param>
/// <param name="ExpectedAlternates">Current alternate-form identities in their required order.</param>
/// <param name="Alternates">Requested order of exactly those alternate forms.</param>
public sealed record OrderAllomorphsIntent(CanonicalId Target,
    IReadOnlyList<CanonicalId> ExpectedAlternates, IReadOnlyList<CanonicalId> Alternates);

/// <summary>Parses the closed JSON intent for ordering existing alternate forms.</summary>
public static class OrderAllomorphsIntentParser
{
    /// <summary>Reads the owning entry and both ordered identity lists.</summary>
    public static OrderAllomorphsIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "OrderAllomorphs", "target", "expectedAlternates", "alternates");
        return new OrderAllomorphsIntent(SoundSystemIntentParsing.Id(authored, "target"),
            SoundSystemIntentParsing.Ids(authored, "expectedAlternates", ordered: true),
            SoundSystemIntentParsing.Ids(authored, "alternates", ordered: true));
    }
}
