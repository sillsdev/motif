using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Composers;
using Xunit;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// Closed-schema rejection and round-trip tests for <see cref="AuthorFeatureValueIntentParser"/> — the
/// point where an agent's authored JSON for the <c>AuthorFeatureValue</c> construct first becomes a
/// typed <see cref="AuthorFeatureValueIntent"/>. No <c>LcmCache</c> involved: parsing never touches a
/// project.
/// </summary>
public sealed class AuthorFeatureValueIntentParserTests
{
    [Fact]
    public void Parse_ValidJson_ProducesTheIntent()
    {
        var featStruc = CanonicalId.Mint();
        var feature = CanonicalId.Mint();
        var element = Parsed(new { featStruc = featStruc.Value, feature = feature.Value });

        var intent = AuthorFeatureValueIntentParser.Parse(element);

        Assert.Equal(featStruc, intent.FeatStruc);
        Assert.Equal(feature, intent.Feature);
    }

    [Fact]
    public void Parse_MissingFeatStruc_IsRejectedByTheClosedSchema()
    {
        var element = Parsed(new { feature = CanonicalId.Mint().Value });

        Assert.Throws<ContractParseException>(() => AuthorFeatureValueIntentParser.Parse(element));
    }

    [Fact]
    public void Parse_MissingFeature_IsRejectedByTheClosedSchema()
    {
        var element = Parsed(new { featStruc = CanonicalId.Mint().Value });

        Assert.Throws<ContractParseException>(() => AuthorFeatureValueIntentParser.Parse(element));
    }

    [Fact]
    public void Parse_UnknownProperty_IsRejectedByTheClosedSchema()
    {
        var element = Parsed(new
        {
            featStruc = CanonicalId.Mint().Value, feature = CanonicalId.Mint().Value, extra = "not allowed",
        });

        Assert.Throws<ContractParseException>(() => AuthorFeatureValueIntentParser.Parse(element));
    }

    [Fact]
    public void Parse_NotAnObject_IsRejectedByTheClosedSchema()
    {
        using var document = JsonDocument.Parse("[]");

        Assert.Throws<ContractParseException>(() => AuthorFeatureValueIntentParser.Parse(document.RootElement));
    }

    [Fact]
    public void Parse_FeatStrucIsNotAValidCanonicalId_IsRejected()
    {
        var element = Parsed(new { featStruc = "not-a-canonical-id", feature = CanonicalId.Mint().Value });

        Assert.Throws<ContractParseException>(() => AuthorFeatureValueIntentParser.Parse(element));
    }

    private static JsonElement Parsed(object value)
    {
        var json = JsonSerializer.Serialize(value);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
