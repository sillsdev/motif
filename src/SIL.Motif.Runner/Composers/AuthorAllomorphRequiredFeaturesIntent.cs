using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Creates the initially empty Required Features structure of an ordinary affix allomorph.</summary>
/// <param name="Allomorph">An existing MoAffixAllomorph with an empty MsEnvFeatures owning slot.</param>
public sealed record AuthorAllomorphRequiredFeaturesIntent(CanonicalId Allomorph);

/// <summary>Parses the closed allomorph Required Features document.</summary>
public static class AuthorAllomorphRequiredFeaturesIntentParser
{
    public static AuthorAllomorphRequiredFeaturesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "AuthorAllomorphRequiredFeatures", "allomorph");
        return new(SoundSystemIntentParsing.Id(authored, "allomorph"));
    }
}
