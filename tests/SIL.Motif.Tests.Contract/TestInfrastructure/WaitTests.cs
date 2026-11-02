using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Sdk;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class WaitTests
{
    [Fact]
    public void AWaitEndsAsSoonAsTheConditionHolds()
    {
        var polls = 0;

        Wait.Until(() => ++polls == 3, "the third poll", TimeSpan.FromMinutes(5));

        Assert.Equal(3, polls);
    }

    [Fact]
    public void AWaitThatTimesOutFailsWithWhatItWaitedForAndTheStateItSaw()
    {
        var failure = Assert.Throws<FailException>(() => Wait.Until(
            () => false, "the worker to stop", TimeSpan.FromMilliseconds(30), () => "state=running"));

        Assert.Equal("the worker to stop; state=running", failure.Message);
    }

    [Fact]
    public void BetweenRunsBeforeEverySleepAndOnceMoreBeforeTheLastLook()
    {
        var pumps = 0;
        var late = false;

        Wait.Until(() => late, "work that only the pump finishes", TimeSpan.FromMinutes(5),
            between: () => { if (++pumps == 4) late = true; });

        Assert.Equal(4, pumps);
    }

    [Fact]
    public void TheLastLookAfterTheDeadlineStillCountsAsSuccess()
    {
        var pumped = false;

        var held = Wait.For(() => pumped, TimeSpan.Zero, between: () => pumped = true);

        Assert.True(held);
    }

    [Fact]
    public void AConditionIsTriedAtLeastTheMinimumNumberOfTimesWhateverTheClockSays()
    {
        var tries = 0;

        var held = Wait.For(() => ++tries >= 5, TimeSpan.Zero, minimumPasses: 5);

        Assert.True(held);
        Assert.Equal(5, tries);
    }

    [Fact]
    public async Task TheAsynchronousWaitEndsWhenAnotherTaskMakesTheConditionHold()
    {
        var done = false;
        _ = Task.Run(async () => { await Task.Delay(40); done = true; });

        await Wait.UntilAsync(() => Volatile.Read(ref done), "the other task", TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task TheAsynchronousWaitFailsLikeTheBlockingOne()
    {
        var failure = await Assert.ThrowsAsync<FailException>(() => Wait.UntilAsync(
            () => false, "a file that never appears", TimeSpan.FromMilliseconds(30)));

        Assert.Equal("a file that never appears", failure.Message);
    }

    [Fact]
    public void AWaitWithoutAReasonNamesTheConditionItWaitedFor()
    {
        var ready = false;

        var failure = Assert.Throws<FailException>(() => Wait.Until(() => ready, timeout: TimeSpan.FromMilliseconds(30)));

        Assert.Equal("Timed out waiting for ready", failure.Message);
    }

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData("", 1.0)]
    [InlineData("1", 1.0)]
    [InlineData("2.5", 2.5)]
    public void TheScaleReadsAsANumberOfAtLeastOne(string? value, double expected) =>
        Assert.Equal(expected, Wait.ParseScale(value));

    [Theory]
    [InlineData("0.5")]
    [InlineData("0")]
    [InlineData("fast")]
    [InlineData("NaN")]
    public void AScaleBelowOneOrNotANumberIsRefused(string value) =>
        Assert.Throws<InvalidOperationException>(() => Wait.ParseScale(value));

    [Fact]
    public void AScaleStretchesEveryDeadlineTakenThroughTheClass() =>
        Assert.Equal(TimeSpan.FromSeconds(10) * Wait.Scale, Wait.Scaled(TimeSpan.FromSeconds(10)));
}
