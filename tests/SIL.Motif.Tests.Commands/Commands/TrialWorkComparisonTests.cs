using SIL.Motif.Commands;
using SIL.Motif.Host.Parser;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class TrialWorkComparisonTests
{
    [Fact]
    public void ComparesTheTrialWordsAndListsTheLargestIncreases()
    {
        var before = new[] { Word("a", 100), Word("b", 200), Word("extra", 9000) };
        var after = new[] { Word("a", 108), Word("b", 210) };

        var result = TrialWorkComparison.Compare(before, after);

        Assert.Equal(300, result.BaselineWork);
        Assert.Equal(318, result.TrialWorkSteps);
        Assert.Equal(6, result.PercentChange);
        var increase = Assert.Single(result.IncreasedWords);
        Assert.Equal("a", increase.Word);
        Assert.Equal(8, increase.PercentMore);
    }

    [Fact]
    public void MissingAndStoppedEvidenceDoesNotProduceAComparison()
    {
        var before = new[] { Word("a", 100), Word("b", 200) };
        var after = new[] { Word("a", 100) with { Outcome = "capped" }, Word("b", 200) with { Outcome = "timed-out" } };
        var result = TrialWorkComparison.Compare(before, after);
        Assert.Null(result.PercentChange);
        Assert.Equal(1, result.WorkLimitStops);
        Assert.Equal(1, result.TimeSafetyNetStops);
        Assert.Equal(["a"], result.WorkLimitWords);
        Assert.Equal(["b"], result.TimeSafetyNetWords);
        Assert.Null(TrialWorkComparison.Compare([], [Word("a", 1)]).BaselineWork);
    }

    private static AssessedWord Word(string word, long work) => new(word, "analysed", []) { WorkSteps = work };
}
