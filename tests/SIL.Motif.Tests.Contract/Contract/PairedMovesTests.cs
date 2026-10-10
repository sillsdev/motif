using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Contract;

public sealed class PairedMovesTests
{
    [Theory]
    [InlineData(80.0, 80.04, false)]
    [InlineData(80.0, 80.05, true)]
    [InlineData(80.3, 80.35, true)]
    [InlineData(80.0, 79.9, true)]
    public void APercentMovesByAtLeastTheThreshold(double before, double after, bool moved) =>
        Assert.Equal(moved, PairedMoves.Percent(before, after));

    [Theory]
    [InlineData(100.0, 104.0, false)]
    [InlineData(100.0, 106.0, true)]
    [InlineData(106.0, 100.0, true)]
    public void ATimeMovesByMoreThanAShareOfTheLargerTime(double before, double after, bool moved) =>
        Assert.Equal(moved, PairedMoves.Time(before, after));

    [Fact]
    public void AValueOnOneSideOnlyMovesAndTwoMissingValuesDoNot()
    {
        Assert.True(PairedMoves.Percent(null, 50));
        Assert.True(PairedMoves.Time(10, null));
        Assert.True(PairedMoves.Count(null, 0));
        Assert.False(PairedMoves.Percent(null, null));
        Assert.False(PairedMoves.Time(null, null));
        Assert.False(PairedMoves.Count(null, null));
    }

    [Fact]
    public void CountsAndTextMoveOnAnyDifference()
    {
        Assert.False(PairedMoves.Count(3, 3));
        Assert.True(PairedMoves.Count(3, 4));
        Assert.False(PairedMoves.Text("12 ms", "12 ms"));
        Assert.True(PairedMoves.Text("12 ms", "13 ms"));
    }
}
