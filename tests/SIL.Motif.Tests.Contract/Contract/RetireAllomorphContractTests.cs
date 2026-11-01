using System;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class RetireAllomorphContractTests
{
    private static RetireAllomorphIntentDocument Example()
    {
        var retirement = AllomorphRetirementContractTests.Example().Retirements[0];
        return new(retirement, [retirement.RoleReplacements[0].Replacement]);
    }

    [Fact]
    public void StandaloneDuplicateRetirementRoundTripsWithoutRuleAuthoring()
    {
        var json = RetireAllomorphCodec.ToJson(Example());
        var parsed = RetireAllomorphCodec.Parse(json);

        Assert.Equal("motif-retire-allomorph", parsed.Format);
        Assert.Equal(1, parsed.Version);
        Assert.Equal(Example().Retirement.Entry, parsed.Retirement.Entry);
        Assert.Equal(Example().Retirement.RetiredForms[0].Id, parsed.Retirement.RetiredForms[0].Id);
        Assert.Equal(Example().Retirement.RoleReplacements[0].Replacement.Id,
            parsed.Retirement.RoleReplacements[0].Replacement.Id);
        Assert.Equal(Example().DuplicateSurvivors[0].Id, parsed.DuplicateSurvivors[0].Id);
        Assert.Equal(json, RetireAllomorphCodec.ToJson(parsed));
        Assert.Equal(RetireAllomorphCodec.IntentDigest(parsed),
            RetireAllomorphCodec.IntentDigest(RetireAllomorphCodec.Parse(json)));
    }

    [Fact]
    public void StandaloneRetirementRefusesUnknownPropertiesAndStemScope()
    {
        var json = JsonNode.Parse(RetireAllomorphCodec.ToJson(Example()))!.AsObject();
        json["retirement"]!["unexpected"] = true;
        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(json.ToJsonString()));

        json = JsonNode.Parse(RetireAllomorphCodec.ToJson(Example()))!.AsObject();
        json["retirement"]!["scope"] = "stem";
        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(json.ToJsonString()));

        json = JsonNode.Parse(RetireAllomorphCodec.ToJson(Example()))!.AsObject();
        json.Remove("duplicateSurvivors");
        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(json.ToJsonString()));
    }

    private static JsonObject StemDocument(bool withReferences)
    {
        var json = JsonNode.Parse(RetireAllomorphCodec.ToJson(Example()))!.AsObject();
        var retirement = json["retirement"]!;
        retirement["scope"] = "stem";
        foreach (var form in new[] { retirement["retiredForms"]![0]!, json["duplicateSurvivors"]![0]! })
        {
            form["class"] = "MoStemAllomorph";
            form["position"] = "stem";
            form["stemName"] = null;
        }
        if (withReferences) return json;
        retirement["roleReplacements"] = new JsonArray();
        retirement["bundles"] = new JsonArray();
        retirement["adhocReplacements"] = new JsonArray();
        return json;
    }

    [Fact]
    public void StandaloneStemRetirementRoundTripsWhenItMovesNoReferences()
    {
        var parsed = RetireAllomorphCodec.Parse(StemDocument(withReferences: false).ToJsonString());

        Assert.Equal(AllomorphRetirementScope.Stem, parsed.Retirement.Scope);
        Assert.Equal(RetireAllomorphCodec.ToJson(parsed),
            RetireAllomorphCodec.ToJson(RetireAllomorphCodec.Parse(RetireAllomorphCodec.ToJson(parsed))));
    }

    [Fact]
    public void StandaloneStemRetirementRefusesReferencesToMoveAndADifferentStemName()
    {
        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(StemDocument(withReferences: true).ToJsonString()));

        var json = StemDocument(withReferences: false);
        json["duplicateSurvivors"]![0]!["stemName"] = Example().Retirement.RetiredForms[0].Id;
        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(json.ToJsonString()));
    }

    [Fact]
    public void StandaloneRetirementRequiresEverySourceToHaveAMeasuredDuplicateSurvivor()
    {
        var json = JsonNode.Parse(RetireAllomorphCodec.ToJson(Example()))!.AsObject();
        json["duplicateSurvivors"] = new JsonArray();

        Assert.Throws<FormatException>(() => RetireAllomorphCodec.Parse(json.ToJsonString()));
    }
}
