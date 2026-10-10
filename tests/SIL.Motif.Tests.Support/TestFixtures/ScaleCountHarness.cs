namespace SIL.Motif.Tests.TestFixtures;

public sealed record ScaleControlCount(string Key, int Count, int MaximumDepth);

/// <summary>Repository reads observed in one measured asynchronous flow.</summary>
public readonly record struct ScaleRepositoryReadCount(long Queries, long RecordsDeserialized,
    long BaselineJsonPayloadBytes = 0, long BaselineOccurrenceTuples = 0);

/// <summary>Counts this async flow's reads; nested scopes are separate and outside reads are ignored.</summary>
public sealed class ScaleRepositoryReadObservation : IDisposable
{
    private readonly SIL.Motif.Worker.Store.RepositoryReadCounters.RepositoryReadScope _scope;

    internal ScaleRepositoryReadObservation(
        SIL.Motif.Worker.Store.RepositoryReadCounters.RepositoryReadScope scope) => _scope = scope;

    /// <summary>Returns the counts recorded by this observation so far.</summary>
    public ScaleRepositoryReadCount Snapshot()
    {
        var counts = _scope.Snapshot();
        return new ScaleRepositoryReadCount(counts.Queries, counts.RecordsDeserialized,
            counts.BaselineJsonPayloadBytes, counts.BaselineOccurrenceTuples);
    }

    /// <summary>Stops counting reads in the current async flow.</summary>
    public void Dispose() => _scope.Dispose();
}

public sealed class ScaleCountSnapshot(IReadOnlyList<ScaleControlCount> motifControls)
{
    public IReadOnlyList<ScaleControlCount> MotifControls { get; } = motifControls;

    public ScaleControlCount MotifControl(string key) => Find(MotifControls, key);

    private static ScaleControlCount Find(IReadOnlyList<ScaleControlCount> counts, string key) =>
        counts.FirstOrDefault(count => count.Key == key) ?? new ScaleControlCount(key, 0, 0);
}

public static class ScaleCountHarness
{
    /// <summary>Counts reads here and in awaited child flows; await all reads before snapshotting or disposal.</summary>
    public static ScaleRepositoryReadObservation ObserveRepositoryReads() =>
        new(SIL.Motif.Worker.Store.RepositoryReadCounters.BeginScope());

    public static int CountLiveViewModels(IEnumerable<WeakReference<object>> references, Type type)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(type);
        return references.Count(reference => reference.TryGetTarget(out var value) && type.IsInstanceOfType(value));
    }

    /// <summary>After the UI settles, forces two full collections and checks a creation-time weak-reference budget.</summary>
    public static int AssertLiveViewModelBudget(IEnumerable<WeakReference<object>> references, Type type,
        int minimum, int maximum)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(type);
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);
        var count = CountLiveViewModels(references, type);
        if (count < minimum || count > maximum)
            throw new InvalidOperationException($"Expected {type.Name} live count in {minimum}..{maximum}; found {count}.");
        return count;
    }

    public static IReadOnlyList<WeakReference<object>> ObserveViewModels<TViewModel>(
        IEnumerable<TViewModel> viewModels, params Type[] viewModelTypes) where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(viewModels);
        ArgumentNullException.ThrowIfNull(viewModelTypes);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        return viewModels.Where(model => model is not null && viewModelTypes.Any(type => type.IsInstanceOfType(model)) &&
                seen.Add(model))
            .Select(model => new WeakReference<object>(model))
            .ToArray();
    }

    public static ScaleCountSnapshot Capture<TControl>(
        TControl root,
        IEnumerable<TControl> descendants,
        Func<TControl, int> depth,
        Func<TControl, string> key,
        Func<TControl, bool> isMotifOwned)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(descendants);
        ArgumentNullException.ThrowIfNull(depth);
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(isMotifOwned);

        var controls = new Dictionary<string, CountAccumulator>(StringComparer.Ordinal);
        foreach (var control in new[] { root }.Concat(descendants))
        {
            if (isMotifOwned(control)) Add(controls, key(control), depth(control));
        }

        return new ScaleCountSnapshot(Freeze(controls));
    }

    private static void Add(Dictionary<string, CountAccumulator> counts, string key, int depth)
    {
        if (!counts.TryGetValue(key, out var count)) counts.Add(key, count = new CountAccumulator());
        count.Count++;
        count.MaximumDepth = Math.Max(count.MaximumDepth, depth);
    }

    private static IReadOnlyList<ScaleControlCount> Freeze(Dictionary<string, CountAccumulator> counts) =>
        counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new ScaleControlCount(pair.Key, pair.Value.Count, pair.Value.MaximumDepth)).ToArray();

    private sealed class CountAccumulator
    {
        public int Count { get; set; }
        public int MaximumDepth { get; set; }
    }
}
