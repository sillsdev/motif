using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>
/// Adds up the wall time of each named stage of one Parsimony job, per side, so the status can show where a
/// run's time went. It measures only; it never decides what a stage does.
/// </summary>
internal sealed class ParsimonyStageTimer
{
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly List<(string Stage, string? Side)> _order = [];
    private readonly Dictionary<(string Stage, string? Side), long> _elapsed = [];

    public ParsimonyStageTimer(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        _started = _clock.GetTimestamp();
    }

    /// <summary>Runs <paramref name="work"/> and adds its wall time to the stage.</summary>
    public T Time<T>(string stage, string? side, Func<T> work)
    {
        var begun = _clock.GetTimestamp();
        try { return work(); }
        finally { Add(stage, side, begun); }
    }

    /// <summary>Runs <paramref name="work"/> and adds its wall time to the stage.</summary>
    public void Time(string stage, string? side, Action work) => Time(stage, side, () =>
    {
        work();
        return 0;
    });

    /// <summary>Awaits <paramref name="work"/> and adds its wall time to the stage.</summary>
    public async Task<T> TimeAsync<T>(string stage, string? side, Func<Task<T>> work)
    {
        var begun = _clock.GetTimestamp();
        try { return await work().ConfigureAwait(false); }
        finally { Add(stage, side, begun); }
    }

    /// <summary>The stages in the order they first ran, then <c>total</c> for the whole job so far.</summary>
    public IReadOnlyList<ParsimonyStageTiming> Snapshot() =>
    [
        .. _order.Select(key => new ParsimonyStageTiming(key.Stage, key.Side, _elapsed[key])),
        new ParsimonyStageTiming("total", null, Milliseconds(_started)),
    ];

    private void Add(string stage, string? side, long begun)
    {
        var key = (stage, side);
        if (!_elapsed.TryGetValue(key, out var known)) _order.Add(key);
        _elapsed[key] = known + Milliseconds(begun);
    }

    private long Milliseconds(long begun) =>
        (long)_clock.GetElapsedTime(begun).TotalMilliseconds;
}
