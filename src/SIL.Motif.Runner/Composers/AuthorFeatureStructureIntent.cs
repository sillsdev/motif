using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>Gives a stem or inflectional affix MSA an initially empty feature structure.</summary>
/// <param name="Msa">
/// An existing MoStemMsa or MoInflAffMsa with an empty MsFeatures or InflFeats slot respectively.
/// Inflectional affix features are requirements on the word. Other MSA kinds and occupied slots are refused.
/// </param>
public sealed record AuthorFeatureStructureIntent(CanonicalId Msa);

/// <summary>
/// Parses the JSON shape an agent authors for <see cref="AuthorFeatureStructureComposer"/>:
/// <c>{ "msa": "..." }</c>. Closed to any other property, exactly like a Layer-0 operation payload.
/// </summary>
public static class AuthorFeatureStructureIntentParser
{
    private const string ConstructName = "AuthorFeatureStructure";
    private static readonly string[] AllowedProperties = { "msa" };

    public static AuthorFeatureStructureIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, AllowedProperties);
        if (!authored.TryGetProperty("msa", out var msaElement) || msaElement.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': 'msa' is required and must be a string.");

        var text = msaElement.GetString()!;
        if (!CanonicalId.TryParse(text, out var msa, out var error))
            throw new ContractParseException($"'{ConstructName}': 'msa' ('{text}') is not a valid canonical id: {error}");

        return new AuthorFeatureStructureIntent(msa);
    }
}
