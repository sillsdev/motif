using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Worker.Review;

public sealed class TrialReviewWorkTests
{
    [Fact]
    public void SharedParserRequestCountsOnceEvenWhenCasesHaveDifferentNativeIdentities()
    {
        var cases = new[]
        {
            new ReviewPlannedCase("a", "native-a", "cat", "ws", null, [], []),
            new ReviewPlannedCase("b", "native-b", "cat", "ws", null, [], []),
            new ReviewPlannedCase("c", "native-c", "dog", "ws", null, [], [])
        };
        var inputs = new FrozenReviewInputs("project", "proposal", "scope", "manifest", null, [], [], cases);
        var scope = new ReviewWorkScope("scope", "baseline", "assessor", "parser", "counter", "settings", "tokeniser");
        var work = new ReviewWorkInputs(scope, scope, ["a", "b", "c"],
            [new("a", true, 100), new("b", true, 100), new("c", true, 100)],
            [new("a", true, 140), new("b", true, 140), new("c", true, 100)]);
        var reduced = TrialReviewWork.DeduplicateRequests(inputs, work);
        Assert.Equal(20, ReviewWorkComparison.Compare(reduced).PercentChange);
        Assert.False(ReviewWorkComparison.IsRedFlag(reduced));
        Assert.Equal(2, reduced.RequiredCases.Count);
        Assert.Null(ReviewWorkComparison.Compare(TrialReviewWork.DeduplicateRequests(inputs,
            work with { After = [new("a", true, 140), new("b", false, 140), new("c", true, 100)] })).PercentChange);
    }
}
