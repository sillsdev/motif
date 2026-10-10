using System;
using System.Linq;
using System.Text.Json.Nodes;
using SIL.Motif.Contract.Retirement;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public class RetirementReviewContractTests
{
    private const string Digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static RetirementReviewStatistics Example() => new(
        new(3, 1, 0, 0), new(2, 1, 0, 0), new(1, 0, 0, 1), 3,
        [new(false, false, 1, 2), new(false, true, 0, 0), new(true, false, 1, 1), new(true, true, 0, 0)],
        0, 1, 4, 0, new("finding-key", 2, 2, 0, 1, Digest, Digest, true), Digest);

    [Fact]
    public void CountsSeparateDistinctIdsCasesOpinionsAndRuleOccurrences()
    {
        var value = Example();
        var read = RetirementReviewCodec.Parse(RetirementReviewCodec.ToJson(value));
        Assert.Equal(4, read.BundlesRepointed.Total());
        Assert.Equal(3, read.AnalysesRepointed.Total());
        Assert.Equal(2, read.WordformsRepointed.Total());
        Assert.Equal(1, read.WordformsRepointed.Mixed);
        Assert.Equal(3, read.AssessedFormCases);
        Assert.Equal(2, read.Adhoc.Sum(c => c.Rules));
        Assert.Equal(3, read.Adhoc.Sum(c => c.TargetOccurrences));
        Assert.Equal(Digest, read.DetailManifestDigest);
    }

    [Fact]
    public void NegativeCountsDuplicatePartitionsAndExtraFieldsAreRefused()
    {
        var json = JsonNode.Parse(RetirementReviewCodec.ToJson(Example()))!;
        json["bundlesRepointed"]!["approved"] = -1;
        Assert.Throws<FormatException>(() => RetirementReviewCodec.Parse(json.ToJsonString()));
        json = JsonNode.Parse(RetirementReviewCodec.ToJson(Example()))!;
        json["adhoc"]![1]!["enabled"] = false;
        Assert.Throws<FormatException>(() => RetirementReviewCodec.Parse(json.ToJsonString()));
        json = JsonNode.Parse(RetirementReviewCodec.ToJson(Example()))!;
        json["averageScore"] = 1;
        Assert.Throws<FormatException>(() => RetirementReviewCodec.Parse(json.ToJsonString()));
    }

    [Fact]
    public void ComputedTotalsAreNotAcceptedAsWireInput()
    {
        var json = JsonNode.Parse(RetirementReviewCodec.ToJson(Example()))!;
        json["bundlesRepointed"]!["total"] = 999;
        Assert.Throws<FormatException>(() => RetirementReviewCodec.Parse(json.ToJsonString()));
    }
}
