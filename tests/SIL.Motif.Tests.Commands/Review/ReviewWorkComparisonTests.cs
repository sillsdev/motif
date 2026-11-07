using SIL.Motif.Contract.Review;
using SIL.Motif.Worker.Runtime.Review;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class ReviewWorkComparisonTests
{
    private static ReviewWorkScope Scope => new("scope", "baseline", "parser", "hash", "counter-v1", "limits", "tokeniser");
    private static ReviewWorkInputs Inputs(long before, long after) =>
        new(Scope, Scope, ["word"], [new("word", true, before)], [new("word", true, after)]);

    [Fact]
    public void ExactlyTwentyDoesNotFlag()
    {
        var result = ReviewWorkComparison.Compare(Inputs(100, 120));
        Assert.Equal(20, result.PercentChange);
        Assert.False(ReviewWorkComparison.IsRedFlag(Inputs(100, 120)));
    }

    [Fact]
    public void TwentyPointZeroZeroOneFlagsBeforeRounding()
    {
        Assert.True(ReviewWorkComparison.IsRedFlag(Inputs(100_000, 120_001)));
    }

    [Fact]
    public void ScopeTotalIsRatioOfSumsNotMeanOfWordRatios()
    {
        var input = new ReviewWorkInputs(Scope, Scope, ["a", "b"],
            [new("a", true, 1), new("b", true, 99)], [new("a", true, 2), new("b", true, 99)]);
        Assert.Equal(1, ReviewWorkComparison.Compare(input).PercentChange);
    }

    [Fact]
    public void RepeatedTokensDoNotMultiplyWork()
    {
        var input = Inputs(100, 120) with { RequiredCases = ["word", "word"] };
        Assert.Equal(20, ReviewWorkComparison.Compare(input).PercentChange);
        Assert.Equal(1, ReviewWorkComparison.Compare(input).WordCount);
    }

    [Fact]
    public void ZeroBaselineCannotProduceInfinity()
    {
        var result = ReviewWorkComparison.Compare(Inputs(0, 1));
        Assert.Null(result.PercentChange);
        Assert.Contains("zero-baseline-work", result.ReasonCodes);
    }

    [Fact]
    public void StoppedOrMissingWordInvalidatesTotal()
    {
        var stopped = Inputs(100, 120) with { After = [new("word", false, 120)] };
        Assert.Null(ReviewWorkComparison.Compare(stopped).PercentChange);
        Assert.Null(ReviewWorkComparison.Compare(stopped with { After = [] }).PercentChange);
    }

    [Fact]
    public void ExtraBaselineWordsAreExcludedExplicitly()
    {
        var input = Inputs(100, 120) with { Before = [new("word", true, 100), new("extra", true, 500)] };
        Assert.Equal(20, ReviewWorkComparison.Compare(input).PercentChange);
        Assert.Contains("baseline-extra-cases-excluded", ReviewWorkComparison.Compare(input).ReasonCodes);
    }

    [Fact]
    public void HugeCountersDoNotOverflowOrRoundTheThreshold()
    {
        var input = Inputs(long.MaxValue - 100, long.MaxValue);
        Assert.NotNull(ReviewWorkComparison.Compare(input).PercentChange);
        Assert.False(ReviewWorkComparison.IsRedFlag(input));
    }

    [Fact]
    public void ParserLimitsOrCounterVersionMismatchIsUnavailable()
    {
        Assert.Null(ReviewWorkComparison.Compare(Inputs(100, 120) with
            { AfterScope = Scope with { SettingsDigest = "other" } }).PercentChange);
        Assert.Null(ReviewWorkComparison.Compare(Inputs(100, 120) with
            { AfterScope = Scope with { CounterSemantics = "other" } }).PercentChange);
        Assert.Null(ReviewWorkComparison.Compare(Inputs(100, 120) with
            { AfterScope = Scope with { ParserDigest = null } }).PercentChange);
        Assert.Null(ReviewWorkComparison.Compare(Inputs(-1, 120)).PercentChange);
    }
}
