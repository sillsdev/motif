using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewGainTests
{
    [Fact]
    public void AWordInTheTextsIsAnAttestedGainAndAGeneratedProbeIsNot()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("written", false, true, [Token("t1")]));
        accumulator.Add(Case("probe", false, true, [Cell("c1")]));
        accumulator.Add(Case("touched", false, true, []));
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(2, facts.GeneratedGained);
    }

    [Fact]
    public void AnApprovedAnalysisMakesAGainedWordAttestedWithoutATokenMembership()
    {
        var approved = new ReviewAssertion("a", "reading", null, ReferenceAuthority.Human, "actor", "rev", true, false, true, []);
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("approved", false, true, []) with { Assertions = [approved] });
        accumulator.Add(Case("reference", false, true, [new("ref", "r", "reference", 1, null, null, [])]));
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(1, facts.GeneratedGained);
    }

    [Fact]
    public void OnlyANoParseToParseChangeIsAGain()
    {
        var accumulator = new ReviewSummaryAccumulator();
        accumulator.Add(Case("already", true, true, [Token("a")]));
        accumulator.Add(Case("lost", true, false, [Token("b")]));
        accumulator.Add(Case("still", false, false, [Token("c")]));
        accumulator.Add(Case("unknown", false, null, [Token("d")]));
        var facts = accumulator.Snapshot();
        Assert.Equal(0, facts.AttestedGained);
        Assert.Equal(0, facts.GeneratedGained);
    }

    [Fact]
    public void ADuplicateCaseAndAWordInTwoSetsCountOnce()
    {
        var accumulator = new ReviewSummaryAccumulator();
        var both = Case("both", false, true, [Cell("c"), Token("t")]);
        accumulator.Add(both);
        accumulator.Add(both);
        var facts = accumulator.Snapshot();
        Assert.Equal(1, facts.AttestedGained);
        Assert.Equal(0, facts.GeneratedGained);
    }

    private static ReviewMembership Token(string id) => new("tokens", id, "sampled-tokens", 1, null, null, []);
    private static ReviewMembership Cell(string id) => new("cells", id, "paradigm-cells", 1, id, "v", []);
    private static ReviewCaseDelta Case(string key, bool? before, bool? after, IReadOnlyList<ReviewMembership> memberships) =>
        new(key, key, key, "ws", false, false, false, [], [], memberships, [])
        { Before = Side(before), After = Side(after) };
    private static ReviewCaseSideFacts Side(bool? parsed) =>
        new(parsed.HasValue ? Completion.Complete : Completion.NotMeasured, parsed, [], [], [], []);
}
