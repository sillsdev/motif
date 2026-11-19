using System.Diagnostics;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Shares a tracked job's remaining cancellation budget with the parser operations it owns.</summary>
internal sealed class PanGlossCleanupScope
{
    private static readonly AsyncLocal<PanGlossCleanupScope?> Active = new();
    private long _deadline = long.MaxValue;
    private int _unconfirmed;

    internal static PanGlossCleanupScope? Current => Active.Value;
    internal bool Unconfirmed => Volatile.Read(ref _unconfirmed) != 0;

    internal void SetDeadline(long? timestamp)
    {
        if (timestamp is { } deadline) Volatile.Write(ref _deadline, deadline);
    }

    internal void MarkUnconfirmed() => Interlocked.Exchange(ref _unconfirmed, 1);

    internal TimeSpan Limit(TimeSpan maximum)
    {
        var deadline = Volatile.Read(ref _deadline);
        if (deadline == long.MaxValue) return maximum;
        var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), deadline);
        return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < maximum ? remaining : maximum;
    }

    internal static IDisposable Use(PanGlossCleanupScope? scope)
    {
        var previous = Active.Value;
        Active.Value = scope;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(PanGlossCleanupScope? previous) : IDisposable
    {
        public void Dispose() => Active.Value = previous;
    }
}
