using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract.Contract;

public sealed class ProposalDifferenceCaseFactsTests
{
    [Fact]
    public void EmptyPairHasPinnedCanonicalDigestAndRoundTrips()
    {
        var checkpoint = new HumanAgreementFacts(HumanAgreementEvidenceAvailability.Available,
            HumanAgreementSearchCompletion.Complete, [], HumanAgreementApprovalState.NoApprovedExpectations,
            0, 0, 0, [], [], [], [], [], [], [], HumanAgreementSpellingAcceptance.NotApplicable, []);
        var pair = new ProposalDifferenceCaseFacts(new(), checkpoint, checkpoint, true, [], [], [], [], [], []);
        var options = MotifJson.CreateOptions();
        var json = JsonSerializer.Serialize(pair, options);
        Assert.Equal("sha256:d581ff27e465a1ace438dfcb99997b3c194af653096513da6456a9022567b20b", IntentDigest.Sha256Of(Encoding.UTF8.GetBytes(CanonicalJson.Canonicalize(json))));
        var restored = JsonSerializer.Deserialize<ProposalDifferenceCaseFacts>(json, options)!;
        Assert.Equal(pair.Policy, restored.Policy);
        Assert.True(restored.CompleteComparison);
        Assert.False(restored.CouldBeBad);
        Assert.False(restored.Improved);
        Assert.Equal(json, JsonSerializer.Serialize(restored, options));
    }

    [Theory]
    [InlineData(ProposalDifferenceBucket.Regression, "delta.regression")]
    [InlineData(ProposalDifferenceBucket.NewCompetingParse, "delta.new-competing-parse")]
    [InlineData(ProposalDifferenceBucket.Overgeneration, "delta.overgeneration")]
    [InlineData(ProposalDifferenceBucket.NewWrongReading, "delta.new-wrong-reading")]
    [InlineData(ProposalDifferenceBucket.AnalysisChanged, "delta.analysis-changed")]
    [InlineData(ProposalDifferenceBucket.AmbiguityBlowup, "delta.ambiguity-blowup")]
    [InlineData(ProposalDifferenceBucket.LostParse, "delta.lost-parse")]
    [InlineData(ProposalDifferenceBucket.ImprovedApprovedMatch, "delta.improved.approved-match")]
    [InlineData(ProposalDifferenceBucket.ImprovedUnique, "delta.improved.unique")]
    [InlineData(ProposalDifferenceBucket.ImprovedNegativeRejection, "delta.improved.negative-rejection")]
    public void BucketWireCodesArePinned(ProposalDifferenceBucket bucket, string name)
    {
        var membership = new ProposalDifferenceBucketMembership(bucket, [], false);
        var json = JsonSerializer.Serialize(membership, MotifJson.CreateOptions());
        Assert.Equal($"{{\"Bucket\":\"{name}\",\"AnalysisIds\":[],\"ObservedAfterIncomplete\":false}}", json);
    }

    [Theory]
    [InlineData("{\"Bucket\":0,\"AnalysisIds\":[],\"ObservedAfterIncomplete\":false}")]
    [InlineData("{\"Bucket\":\"unknown\",\"AnalysisIds\":[],\"ObservedAfterIncomplete\":false}")]
    public void BucketPropertiesRejectUnknownAndNumericValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProposalDifferenceBucketMembership>(json,
            MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData("{\"Changes\":[0]}")]
    [InlineData("{\"Changes\":[\"invented\"]}")]
    public void MechanicalPropertiesRejectUnknownAndNumericValues(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProposalDifferenceCaseFacts>(json,
            MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData("proposal-difference/v2", 3, 2)]
    [InlineData("proposal-difference/v1", 0, 2)]
    [InlineData("proposal-difference/v1", 3, 0)]
    public void PolicyRefusesUnsupportedVersionsAndNonpositiveThresholds(string version, int absolute, int ratio)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ProposalDifferencePolicy(version, absolute, ratio));
    }

    [Theory]
    [InlineData("{\"Bucket\":\"delta.regression\",\"AnalysisIds\":[],\"ObservedAfterIncomplete\":false,\"Extra\":1}")]
    [InlineData("{\"Bucket\":\"delta.regression\",\"ObservedAfterIncomplete\":false}")]
    [InlineData("{\"Bucket\":\"delta.regression\",\"AnalysisIds\":null,\"ObservedAfterIncomplete\":false}")]
    public void BucketMembershipRefusesExtraMissingAndNullMembers(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProposalDifferenceBucketMembership>(json,
            MotifJson.CreateOptions()));
    }

    [Theory]
    [InlineData("{\"Version\":\"proposal-difference/v1\",\"AbsoluteIncrease\":3}")]
    [InlineData("{\"Version\":\"proposal-difference/v1\",\"AbsoluteIncrease\":3,\"Ratio\":2,\"Extra\":1}")]
    public void PolicyRefusesMissingAndExtraMembers(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ProposalDifferencePolicy>(json,
            MotifJson.CreateOptions()));
    }
}
