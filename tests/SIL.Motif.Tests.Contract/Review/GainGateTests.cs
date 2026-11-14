using System.Text.Json;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Contract.Review;

public sealed class GainGateTests
{
    private static GrammarSizeDelta Objects(long entries) => GrammarSizeDelta.None with { Entries = entries };

    [Fact]
    public void TheDefaultPolicyIsAdvisoryAtOneAndAHalfAttestedTypesPerObject()
    {
        Assert.Equal(GainGateMode.Advisory, GainGatePolicy.Default.Mode);
        Assert.Equal(1.5, GainGatePolicy.Default.MinAttestedPerObject);
    }

    [Fact]
    public void AdvisoryReportsOverThresholdWithoutHolding()
    {
        var gains = GainSummary.Create(attested: 10, generated: 900, Objects(20), GainGatePolicy.Default, incomplete: false);
        Assert.Equal(GainOutcome.UnderThreshold, gains.Gate.Outcome);
        Assert.False(gains.Gate.Hold);
        Assert.Equal(0.5, gains.AttestedPerObject);
        Assert.Equal(2.0, gains.ObjectsPerAttestedType);
    }

    [Fact]
    public void HoldModeHoldsBelowTheThresholdOnly()
    {
        var policy = new GainGatePolicy(GainGateMode.Hold, 1.5);
        Assert.True(GainSummary.Create(29, 0, Objects(20), policy, false).Gate.Hold);
        Assert.False(GainSummary.Create(30, 0, Objects(20), policy, false).Gate.Hold);
        Assert.Equal(GainOutcome.WithinThreshold, GainSummary.Create(30, 0, Objects(20), policy, false).Gate.Outcome);
    }

    [Fact]
    public void GeneratedGainsNeverRaiseTheRatio()
    {
        var policy = new GainGatePolicy(GainGateMode.Hold, 1.5);
        var gains = GainSummary.Create(attested: 0, generated: 924, Objects(46), policy, false);
        Assert.Equal(0.0, gains.AttestedPerObject);
        Assert.True(gains.Gate.Hold);
    }

    [Fact]
    public void AnAddedRuleOrAffixProcessIsExempt()
    {
        var policy = new GainGatePolicy(GainGateMode.Hold, 1.5);
        var withRule = Objects(5) with { PhonologicalRules = 1 };
        var gains = GainSummary.Create(0, 0, withRule, policy, false);
        Assert.Equal(GainOutcome.RuleAdded, gains.Gate.Outcome);
        Assert.False(gains.Gate.Hold);
    }

    [Fact]
    public void NoObjectsOrIncompleteEvidenceOrAnOffPolicyNeverHold()
    {
        var policy = new GainGatePolicy(GainGateMode.Hold, 1.5);
        Assert.Equal(GainOutcome.NoObjectsAdded, GainSummary.Create(0, 0, GrammarSizeDelta.None, policy, false).Gate.Outcome);
        var incomplete = GainSummary.Create(0, 0, Objects(9), policy, incomplete: true);
        Assert.Equal(GainOutcome.Incomplete, incomplete.Gate.Outcome);
        Assert.False(incomplete.Gate.Hold);
        var off = GainSummary.Create(0, 0, Objects(9), new GainGatePolicy(GainGateMode.Off, 1.5), false);
        Assert.Equal(GainOutcome.NotEvaluated, off.Gate.Outcome);
        Assert.False(off.Gate.Hold);
    }

    [Fact]
    public void ThePolicyRejectsANonPositiveThreshold()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GainGatePolicy(GainGateMode.Advisory, 0));
    }

    [Fact]
    public void TheSummaryRoundTripsThroughTheStrictContract()
    {
        var gains = GainSummary.Create(3, 7, Objects(2), GainGatePolicy.Default, false);
        var json = JsonSerializer.Serialize(gains, ReviewJson.Options);
        Assert.Contains("\"attestedGained\":3", json);
        Assert.Contains("\"generatedGained\":7", json);
        Assert.Contains("\"outcome\":\"within-threshold\"", json);
        Assert.Equal(gains, JsonSerializer.Deserialize<GainSummary>(json, ReviewJson.Options));
    }
}
