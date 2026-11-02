using SIL.Motif.Worker.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker.Worker;

public sealed class ParsimonyStageTimerTests
{
    private sealed class ManualClock : TimeProvider
    {
        public long Ticks { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ticks;
    }

    [Fact]
    public void StagesAddUpPerSideInFirstRunOrderWithATotalLast()
    {
        var clock = new ManualClock();
        var timer = new ParsimonyStageTimer(clock);

        timer.Time("facts", "baseline", () => clock.Ticks += 30);
        timer.Time("facts", "candidate", () => clock.Ticks += 50);
        timer.Time("facts", "baseline", () => clock.Ticks += 20);
        var value = timer.Time("evidence", null, () =>
        {
            clock.Ticks += 5;
            return 7;
        });

        var snapshot = timer.Snapshot();

        Assert.Equal(7, value);
        Assert.Equal(["facts/baseline=50", "facts/candidate=50", "evidence/=5", "total/=105"],
            snapshot.Select(t => $"{t.Stage}/{t.Side}={t.Milliseconds}"));
    }

    [Fact]
    public async Task AStageThatThrowsStillCountsItsTime()
    {
        var clock = new ManualClock();
        var timer = new ParsimonyStageTimer(clock);

        await Assert.ThrowsAsync<InvalidOperationException>(() => timer.TimeAsync<int>("import", null, () =>
        {
            clock.Ticks += 9;
            throw new InvalidOperationException();
        }));

        Assert.Equal(9, timer.Snapshot()[0].Milliseconds);
    }
}
