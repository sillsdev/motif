using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using SIL.Motif.Worker.Jobs;
using System.Collections;
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
    [Fact]
    public void WordWorkRowsTraverseEachScopeLinearlyAndOnlyWhenEnumerated()
    {
        long Visits(int count)
        {
            var cases = new CountingCases(Enumerable.Range(0, count).Select(i =>
                new ReviewPlannedCase("case" + i, "key" + i, "word" + i, "ws", null, [], [])).ToArray());
            var inputs = new FrozenReviewInputs("project", "proposal", "scope", "manifest", null, [], [], cases);
            var scope = new ReviewWorkScope("scope", "baseline", "assessor", "parser", "counter", "settings", "tokeniser");
            var rows = Enumerable.Range(0, count).Select(i => new ReviewWorkCase("case" + i, true, 100)).ToArray();
            var work = new ReviewWorkInputs(scope, scope, rows.Select(row => row.CaseKey).ToArray(), rows, rows);
            var items = TrialReviewPublisher.WordWorkRows(inputs, new Dictionary<string, TrialReviewObservation>(),
                new Dictionary<string, TrialReviewObservation>(), work);
            Assert.Equal(0, cases.Visits);
            Assert.Equal(count, items.Count());
            return cases.Visits;
        }
        var small = Visits(200);
        var large = Visits(400);
        Assert.InRange(large, small * 2 - 2, small * 2 + 2);
    }

    private sealed class CountingCases(IReadOnlyList<ReviewPlannedCase> source) : IReadOnlyList<ReviewPlannedCase>
    {
        public long Visits { get; private set; }
        public int Count => source.Count;
        public ReviewPlannedCase this[int index] => source[index];
        public IEnumerator<ReviewPlannedCase> GetEnumerator()
        {
            foreach (var item in source) { Visits++; yield return item; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
