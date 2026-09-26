namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// A clock that stands still until a test moves it, so no assertion depends on the wall clock. Timestamps
/// and elapsed times follow the same instant, so a stopwatch read through it advances only with
/// <see cref="Advance"/>.
/// </summary>
public sealed class FixedClock(DateTimeOffset now, TimeZoneInfo? localTimeZone = null) : TimeProvider
{
    // TimeProvider.GetLocalNow shifts GetUtcNow's clock time by the zone offset, so it must carry offset zero.
    private DateTimeOffset _now = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>The zone local times are read in; UTC unless the test names one.</summary>
    public override TimeZoneInfo LocalTimeZone { get; } = localTimeZone ?? TimeZoneInfo.Utc;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _now.UtcTicks;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by) => _now += by;
}
