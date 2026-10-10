using System;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class FrozenRetirementExpectationTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static string Id(int n) => CanonicalId.FromGuid(Guid.Parse($"00000000-0000-0000-0000-{n:000000000000}")).Value;

    private static FrozenExpectationSet Example() => new(
        new BaselineToken("project", Digest, "projection/v1", "2026-10-07T12:00:00Z", Digest),
        Digest, Digest, "expectation-revision/v1",
        [new(Id(1), Id(2), "seh", "ara", false, false,
            [new(Id(3), Id(4), "approved", Digest, [new(Id(5), Id(6), "approved")],
                [new(Id(7), Id(8), Id(9), null, "whole", null, null, [new("seh", "la", Digest)])])])],
        [], []);

    [Fact]
    public void FrozenInputKeepsOutsideSelectionReadingAndHumanMembership()
    {
        var json = FrozenExpectationCodec.ToJson(Example());
        var captured = FrozenExpectationCodec.Parse(json);
        Assert.False(captured.Cases[0].InSelection);
        Assert.Equal(Id(5), captured.Cases[0].Readings[0].Evaluations[0].Evaluation);
        Assert.Equal(Id(8), captured.Cases[0].Readings[0].Morphs[0].Form);
        Assert.Equal("la", captured.Cases[0].Readings[0].Morphs[0].BundleText[0].Text);
        Assert.Equal(json, FrozenExpectationCodec.ToJson(captured));
        FrozenExpectationCodec.RequireComplete(captured);
    }

    [Fact]
    public void MissingIdentityAndUnknownFrozenFieldsAreRefused()
    {
        var json = JsonNode.Parse(FrozenExpectationCodec.ToJson(Example()))!;
        json["cases"]![0]!["readings"]![0]!["morphs"]![0]!.AsObject().Remove("inflType");
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.Parse(json.ToJsonString()));
        json = JsonNode.Parse(FrozenExpectationCodec.ToJson(Example()))!;
        json["cases"]![0]!["readings"]![0]!["morphs"]![0]!["msa"] = null;
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.Parse(json.ToJsonString()));
        json = JsonNode.Parse(FrozenExpectationCodec.ToJson(Example()))!;
        json["baseline"]!["unknown"] = 1;
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.Parse(json.ToJsonString()));
    }

    [Fact]
    public void IncompleteInputIsRetainedAndCannotClaimCompleteVerification()
    {
        var incomplete = Example() with { Unavailable = ["vernacular-case-manifest-incomplete"] };
        var captured = FrozenExpectationCodec.Parse(FrozenExpectationCodec.ToJson(incomplete));
        Assert.Equal(incomplete.Unavailable, captured.Unavailable);
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.RequireComplete(captured));
    }

    [Fact]
    public void GuessedComparisonDataRemainsConditionalAndSeparateFromBundleText()
    {
        var json = JsonNode.Parse(FrozenExpectationCodec.ToJson(Example()))!;
        var morph = json["cases"]![0]!["readings"]![0]!["morphs"]![0]!;
        morph["guessedString"] = "guess";
        morph["guessedWritingSystem"] = "seh";
        var captured = FrozenExpectationCodec.Parse(json.ToJsonString());
        Assert.Equal("guess", captured.Cases[0].Readings[0].Morphs[0].GuessedString);
        Assert.Equal("la", captured.Cases[0].Readings[0].Morphs[0].BundleText[0].Text);
        morph["guessedWritingSystem"] = null;
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.Parse(json.ToJsonString()));
    }

    [Fact]
    public void IgnoredBaselinePropertiesCannotBypassClosedInput()
    {
        var json = JsonNode.Parse(FrozenExpectationCodec.ToJson(Example()))!;
        json["baseline"]!["semanticIdentity"] = new JsonObject();
        Assert.Throws<FormatException>(() => FrozenExpectationCodec.Parse(json.ToJsonString()));
    }
}
