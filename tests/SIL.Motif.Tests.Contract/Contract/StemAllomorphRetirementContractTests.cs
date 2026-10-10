using System;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class StemAllomorphRetirementContractTests
{
    private static JsonObject StemJson()
    {
        var json = JsonNode.Parse(AllomorphRetirementCodec.ToJson(AllomorphRetirementContractTests.Example()))!.AsObject();
        var retirement = json["retirements"]![0]!;
        retirement["scope"] = "stem";
        foreach (var form in new[]
                 {
                     retirement["retiredForms"]![0]!,
                     retirement["roleReplacements"]![0]!["replacement"]!,
                     retirement["adhocReplacements"]![0]!["replacement"]!
                 })
        {
            form["class"] = "MoStemAllomorph";
            form["position"] = "root";
            form["stemName"] = null;
        }
        return json;
    }

    [Fact]
    public void StemAlternateRoundTripsWithoutEnablingApply()
    {
        var json = StemJson().ToJsonString();
        var intent = AllomorphRetirementCodec.Parse(json);
        var canonical = AllomorphRetirementCodec.ToJson(intent);
        Assert.Contains("\"scope\":\"stem\"", canonical);
        Assert.Contains("\"stemName\":null", canonical);
        Assert.Equal(canonical, AllomorphRetirementCodec.ToJson(AllomorphRetirementCodec.Parse(canonical)));
        Assert.False(AllomorphRetirementCapabilities.CanApply);
    }

    [Theory]
    [InlineData("\"Stem\"")]
    [InlineData("\"STEM\"")]
    [InlineData("\"Affix\"")]
    [InlineData("\"compound\"")]
    [InlineData("\"variant\"")]
    [InlineData("1")]
    [InlineData("null")]
    public void ScopeUsesOnlyClosedLowercaseStrings(string scope)
    {
        var json = StemJson();
        json["retirements"]![0]!["scope"] = JsonNode.Parse(scope);
        Refuses(json);
    }

    [Theory]
    [InlineData("class", "MoAffixAllomorph")]
    [InlineData("class", "MoAffixProcess")]
    [InlineData("location", "lexeme")]
    [InlineData("position", "prefix")]
    [InlineData("position", "bound-root")]
    [InlineData("position", "bound-stem")]
    [InlineData("position", "clitic")]
    [InlineData("position", "phrase")]
    [InlineData("position", "circumfix")]
    public void UnsupportedStemSourcesAreRefused(string property, string value)
    {
        var json = StemJson();
        json["retirements"]![0]!["retiredForms"]![0]![property] = value;
        Refuses(json);
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("stemName")]
    public void ScopeAndNullStemNameMustBeExplicit(string property)
    {
        var json = StemJson();
        var owner = property == "scope" ? json["retirements"]![0]! : json["retirements"]![0]!["retiredForms"]![0]!;
        owner.AsObject().Remove(property);
        Refuses(json);
    }

    [Fact]
    public void SameNamedStemGateRoundTripsAndCanonicalAliasesKeepItsDigest()
    {
        var json = StemJson();
        const string name = "AAAAAAAAAAAAAAAAAAAAZA";
        foreach (var form in Forms(json)) form["stemName"] = name;
        var original = AllomorphRetirementCodec.Parse(json.ToJsonString());
        Assert.Equal(name, original.Retirements[0].RetiredForms[0].StemName);
        Forms(json)[1]["stemName"] = "stemName_" + name;
        Assert.Equal(AllomorphRetirementCodec.IntentDigest(original),
            AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
        foreach (var form in Forms(json)) form["stemName"] = "AAAAAAAAAAAAAAAAAAAAZQ";
        Assert.NotEqual(AllomorphRetirementCodec.IntentDigest(original),
            AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(json.ToJsonString())));
    }

    [Fact]
    public void DifferentOrMissingStemNameGatesRefuseEveryDestinationRoute()
    {
        foreach (var destination in new[] { 1, 2 })
        {
            var json = StemJson();
            Forms(json)[destination]["stemName"] = "AAAAAAAAAAAAAAAAAAAAZA";
            Refuses(json);
            json = StemJson();
            foreach (var form in Forms(json)) form["stemName"] = "AAAAAAAAAAAAAAAAAAAAZA";
            Forms(json)[destination]["stemName"] = null;
            Refuses(json);
            json = StemJson();
            foreach (var form in Forms(json)) form["stemName"] = "AAAAAAAAAAAAAAAAAAAAZA";
            Forms(json)[destination]["stemName"] = "AAAAAAAAAAAAAAAAAAAAZQ";
            Refuses(json);
        }
    }

    [Fact]
    public void StemNameCannotAliasAnotherObjectClass()
    {
        var json = StemJson();
        foreach (var form in Forms(json)) form["stemName"] = json["rule"]!["id"]!.GetValue<string>();
        Refuses(json);
    }

    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    [InlineData("compound")]
    [InlineData("variant")]
    public void StemExpansionRolesAreRefused(string role)
    {
        var json = StemJson();
        json["retirements"]![0]!["roleReplacements"]![0]!["expansionRole"] = role;
        Refuses(json);
    }

    [Fact]
    public void StemVariantInflTypeIsRefused()
    {
        var json = StemJson();
        json["retirements"]![0]!["roleReplacements"]![0]!["inflType"] = "AAAAAAAAAAAAAAAAAAAAZA";
        json["retirements"]![0]!["bundles"]![0]!["inflType"] = "AAAAAAAAAAAAAAAAAAAAZA";
        Refuses(json);
    }

    [Fact]
    public void RootToStemOrAffixDestinationsAreRefused()
    {
        foreach (var destination in new[] { 1, 2 })
        {
            var json = StemJson();
            Forms(json)[destination]["position"] = "stem";
            Refuses(json);
            json = StemJson();
            Forms(json)[destination]["class"] = "MoAffixAllomorph";
            Refuses(json);
        }
        var stem = StemJson();
        foreach (var form in Forms(stem)) form["position"] = "stem";
        Assert.Equal("stem", AllomorphRetirementCodec.Parse(stem.ToJsonString()).Retirements[0].RetiredForms[0].Position);
    }

    [Fact]
    public void PreVersionTwoArtifactsAreRefusedWithoutDefaultScope()
    {
        var json = StemJson();
        json["version"] = 1;
        Assert.Contains("regenerate", Assert.Throws<FormatException>(() =>
            AllomorphRetirementCodec.Parse(json.ToJsonString())).Message);
    }

    [Fact]
    public void StemCapabilityListsAllCommonAndAdditionalProofsWithoutApply()
    {
        Assert.Equal(new[] { AllomorphRetirementScope.Affix, AllomorphRetirementScope.Stem },
            AllomorphRetirementCapabilities.AuthoringScopes);
        var required = AllomorphRetirementCapabilities.RequiredCapabilitiesFor(AllomorphRetirementScope.Stem);
        Assert.All(AllomorphRetirementCapabilities.RequiredCapabilities, proof => Assert.Contains(proof, required));
        Assert.Contains("stem-selection-gate-equivalence", required);
        Assert.Contains("stem-environment-rule-correspondence", required);
        Assert.Contains("stem-expansion-context-census", required);
        Assert.Contains("stem-bundle-form-effects", required);
        Assert.Contains("frozen-stem-reading-verification", required);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AllomorphRetirementCapabilities.RequiredCapabilitiesFor((AllomorphRetirementScope)99));
        Assert.False(AllomorphRetirementCapabilities.CanApply);
    }

    [Fact]
    public void AffixFormsCannotDeclareStemNamesAndScopeChangesAffectDigest()
    {
        var affix = AllomorphRetirementContractTests.Example();
        Assert.NotEqual(AllomorphRetirementCodec.IntentDigest(affix),
            AllomorphRetirementCodec.IntentDigest(AllomorphRetirementCodec.Parse(StemJson().ToJsonString())));
        var json = JsonNode.Parse(AllomorphRetirementCodec.ToJson(affix))!.AsObject();
        Forms(json)[0]["stemName"] = "AAAAAAAAAAAAAAAAAAAAZA";
        Refuses(json);
    }

    private static JsonNode[] Forms(JsonObject json) =>
    [
        json["retirements"]![0]!["retiredForms"]![0]!,
        json["retirements"]![0]!["roleReplacements"]![0]!["replacement"]!,
        json["retirements"]![0]!["adhocReplacements"]![0]!["replacement"]!
    ];

    private static void Refuses(JsonObject json) =>
        Assert.Throws<FormatException>(() => AllomorphRetirementCodec.Parse(json.ToJsonString()));
}
