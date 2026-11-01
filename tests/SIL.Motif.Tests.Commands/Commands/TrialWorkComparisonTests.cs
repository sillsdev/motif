using SIL.Motif.Commands;
using SIL.Motif.Contract.Responses;
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

    [Fact]
    public void ListsEachIncreasedWordWithItsRatioAndTheTotalsOverTheSameWords()
    {
        var before = new[] { Word("a", 100), Word("b", 200), Word("c", 50) };
        var after = new[] { Word("a", 150), Word("b", 190), Word("c", 52) };

        var result = TrialWorkComparison.Compare(before, after);

        Assert.Equal(350, result.BaselineWork);
        Assert.Equal(392, result.TrialWorkSteps);
        Assert.Equal(12, result.PercentChange);
        var increase = Assert.Single(result.IncreasedWords);
        Assert.Equal(("a", 100L, 150L), (increase.Word, increase.BaselineWork, increase.TrialWork));
        Assert.Equal(1.5, increase.Ratio);
        Assert.Equal(50, increase.PercentMore);
    }

    [Fact]
    public void OnlyTheTopFiveWordsOverFivePercentAreListedWorstFirst()
    {
        var before = Enumerable.Range(0, 8).Select(index => Word("w" + index, 100)).ToArray();
        var after = Enumerable.Range(0, 8).Select(index => Word("w" + index, 100 + index * 10)).ToArray();

        var words = TrialWorkComparison.Compare(before, after).IncreasedWords.Select(item => item.Word);

        Assert.Equal(["w7", "w6", "w5", "w4", "w3"], words);
    }

    [Fact]
    public void StopsAreSplitByTheLimitThatFiredAndAWordWhereBothFiredCountsAsTheWorkLimit()
    {
        var before = new[] { Word("a", 1), Word("b", 1), Word("c", 1), Word("d", 1) };
        var after = new[]
        {
            Word("a", 1) with { Outcome = "capped" },
            Word("b", 1) with { Outcome = "timed-out" },
            Word("c", 1) with { Morphology = new ParseWordEvidence("s", 0, "c", 0, true, true, false, [], []) },
            Word("d", 1),
        };

        var result = TrialWorkComparison.Compare(before, after);

        Assert.Equal(["a", "c"], result.WorkLimitWords);
        Assert.Equal(["b"], result.TimeSafetyNetWords);
        Assert.Equal((2, 1), (result.WorkLimitStops, result.TimeSafetyNetStops));
        Assert.Null(result.PercentChange);
    }

    [Fact]
    public void TimeOnThisComputerIsSummedOverTheSameWordsAndNeverDecidesTheWorkComparison()
    {
        var before = new[] { Word("a", 100) with { ElapsedMs = 40 }, Word("b", 100) with { ElapsedMs = 60 } };
        var after = new[] { Word("a", 100) with { ElapsedMs = 90 }, Word("b", 100) with { ElapsedMs = 70 } };

        var result = TrialWorkComparison.Compare(before, after);

        Assert.Equal(0, result.PercentChange);
        Assert.Equal((100d, 160d), (result.BaselineTimeMs, result.TrialTimeMs));
        Assert.Null(TrialWorkComparison.Compare(before, [after[0], Word("b", 100)]).TrialTimeMs);
    }

    [Fact]
    public void TheTrialWorkSerializesTheRatioTheStopKindsAndTheTimeOnThisComputer()
    {
        var before = new[] { Word("a", 100) with { ElapsedMs = 5 }, Word("b", 10) with { ElapsedMs = 5 } };
        var after = new[] { Word("a", 150) with { ElapsedMs = 6 }, Word("b", 10) with { ElapsedMs = 6 } };

        var json = System.Text.Json.JsonSerializer.SerializeToNode(TrialWorkComparison.Compare(before, after),
            SIL.Motif.Contract.MotifJson.CreateOptions())!.AsObject();

        Assert.Equal(160, json["TrialWorkSteps"]!.GetValue<long>());
        Assert.Equal(1.5, json["IncreasedWords"]![0]!["Ratio"]!.GetValue<double>());
        Assert.Equal(10, json["BaselineTimeMs"]!.GetValue<double>());
        Assert.Equal(12, json["TrialTimeMs"]!.GetValue<double>());
        Assert.NotNull(json["WorkLimitWords"]);
        Assert.NotNull(json["TimeSafetyNetWords"]);
        Assert.DoesNotContain("Elapsed", json.ToJsonString(), StringComparison.Ordinal);
    }

    private static AssessedWord Word(string word, long work) => new(word, "analysed", []) { WorkSteps = work };

    [Theory]
    [InlineData(104L, false)]
    [InlineData(105L, false)]
    [InlineData(106L, true)]
    public void WarnsOnlyWhenTotalWorkRisesMoreThanFivePercent(long work, bool warning)
    {
        var result = TrialWorkComparison.Compare([Word("word", 100)], [Word("word", work)]);
        Assert.Equal(warning, result.WorkIncreaseWarning);
    }
}
