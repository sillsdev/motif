using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Gives a stem, inflectional or derivational affix MSA an initially empty feature structure.</summary>
/// <param name="Msa">
/// An existing MoStemMsa, MoInflAffMsa or MoDerivAffMsa with the named slot empty: MsFeatures, InflFeats, or
/// FromMsFeatures and ToMsFeatures. Inflectional affix features are requirements on the word. Other MSA kinds and
/// occupied slots are refused.
/// </param>
/// <param name="Side">
/// Required for a derivational affix and refused for any other MSA: <c>from</c> is the requirement on the stem the
/// affix attaches to, <c>to</c> is what the derived stem carries, replacing the stem's own value of that feature.
/// </param>
public sealed record AuthorFeatureStructureIntent(CanonicalId Msa, string? Side = null);

/// <summary>
/// Parses the JSON shape an agent authors for <see cref="AuthorFeatureStructureComposer"/>:
/// <c>{ "msa": "...", "side": "from" | "to" }</c>, <c>side</c> only for a derivational affix. Closed to any other
/// property, exactly like a Layer-0 operation payload.
/// </summary>
public static class AuthorFeatureStructureIntentParser
{
    private const string ConstructName = "AuthorFeatureStructure";
    private static readonly string[] AllowedProperties = { "msa", "side" };

    public static AuthorFeatureStructureIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, AllowedProperties);
        if (!authored.TryGetProperty("msa", out var msaElement) || msaElement.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': 'msa' is required and must be a string.");

        var text = msaElement.GetString()!;
        if (!CanonicalId.TryParse(text, out var msa, out var error))
            throw new ContractParseException($"'{ConstructName}': 'msa' ('{text}') is not a valid canonical id: {error}");

        string? side = null;
        if (authored.TryGetProperty("side", out var sideElement))
        {
            side = sideElement.ValueKind == JsonValueKind.String ? sideElement.GetString() : null;
            if (side is not ("from" or "to"))
                throw new ContractParseException($"'{ConstructName}': 'side' must be \"from\" or \"to\".");
        }

        return new AuthorFeatureStructureIntent(msa, side);
    }
}
