namespace SIL.Motif.RecordStore;

public sealed class MicrosecondTimeProvider(TimeProvider source) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        var ticks = source.GetUtcNow().UtcTicks;
        return new DateTimeOffset(ticks - ticks % 10, TimeSpan.Zero);
    }
}
