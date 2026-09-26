using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class FixedClockTests
{
    [Fact]
    public void ALocalInstantReadsBackAsTheSameLocalTime()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-minus-five", TimeSpan.FromHours(-5), "test", "test");
        var clock = new FixedClock(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.FromHours(-5)), zone);

        Assert.Equal(TimeSpan.Zero, clock.GetUtcNow().Offset);
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 15, 30, 0, TimeSpan.Zero), clock.GetUtcNow());
        Assert.Equal(new DateTimeOffset(2026, 3, 4, 10, 30, 0, TimeSpan.FromHours(-5)), clock.GetLocalNow());
        Assert.Equal(10, clock.GetLocalNow().Hour);
    }
}
