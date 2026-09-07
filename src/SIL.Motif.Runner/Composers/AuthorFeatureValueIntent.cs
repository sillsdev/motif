using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// The authored input to <see cref="AuthorFeatureValueComposer"/>: add a specification for one
/// existing feature to an already-created <c>FsFeatStruc</c> -- the grammar counterpart to
/// <see cref="AuthorFeatureStructureIntent"/>, which creates the structure this construct populates.
/// </summary>
/// <param name="FeatStruc">
/// The existing <c>FsFeatStruc</c> to add a feature specification to -- typically one
/// <see cref="AuthorFeatureStructureComposer"/> created.
/// </param>
/// <param name="Feature">
/// The existing <c>FsFeatDefn</c> the new specification is for. Must not already have a specification
/// on <paramref name="FeatStruc"/> -- this construct adds one, never replaces one.
/// </param>
public sealed record AuthorFeatureValueIntent(CanonicalId FeatStruc, CanonicalId Feature);

/// <summary>
/// Parses the JSON shape an agent authors for <see cref="AuthorFeatureValueComposer"/>:
/// <c>{ "featStruc": "...", "feature": "..." }</c>. Closed to any other property, exactly like a
/// Layer-0 operation payload.
/// </summary>
public static class AuthorFeatureValueIntentParser
{
    private const string ConstructName = "AuthorFeatureValue";
    private static readonly string[] AllowedProperties = { "featStruc", "feature" };

    public static AuthorFeatureValueIntent Parse(JsonElement authored)
    {
        if (authored.ValueKind != JsonValueKind.Object)
            throw new ContractParseException($"'{ConstructName}': the authored construct must be a JSON object.");

        foreach (var property in authored.EnumerateObject())
        {
            if (property.Name != AllowedProperties[0] && property.Name != AllowedProperties[1])
                throw new ContractParseException($"'{ConstructName}': unknown property '{property.Name}'.");
        }

        return new AuthorFeatureValueIntent(
            RequireCanonicalId(authored, "featStruc"),
            RequireCanonicalId(authored, "feature"));
    }

    private static CanonicalId RequireCanonicalId(JsonElement authored, string propertyName)
    {
        if (!authored.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' is required and must be a string.");

        var text = element.GetString()!;
        if (!CanonicalId.TryParse(text, out var id, out var error))
            throw new ContractParseException($"'{ConstructName}': '{propertyName}' ('{text}') is not a valid canonical id: {error}");

        return id;
    }
}
