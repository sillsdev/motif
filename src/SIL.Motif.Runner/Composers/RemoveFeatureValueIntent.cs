using System.Text.Json;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Composers;

/// <summary>Removes the specification for one feature from a feature structure.</summary>
/// <param name="FeatStruc">The existing feature structure, a stem's MsFeatures or an inflectional affix's InflFeats.</param>
/// <param name="Feature">The feature whose specification is removed.</param>
public sealed record RemoveFeatureValueIntent(CanonicalId FeatStruc, CanonicalId Feature);

/// <summary>Parses the closed feature value removal document.</summary>
public static class RemoveFeatureValueIntentParser
{
    /// <summary>Parses <c>{ featStruc, feature }</c>.</summary>
    /// <exception cref="SIL.Motif.Contract.Parsing.ContractParseException">The document has unknown fields or malformed ids.</exception>
    public static RemoveFeatureValueIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RemoveFeatureValue", "featStruc", "feature");
        return new(SoundSystemIntentParsing.Id(authored, "featStruc"), SoundSystemIntentParsing.Id(authored, "feature"));
    }
}
