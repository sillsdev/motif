using System.Runtime.CompilerServices;
using SIL.LCModel;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// Writes that the operations of one Proposal hold back so a value many of them change is written once: composing, Dry
/// Run and Apply open a batch around their operations and complete it inside the same unit of work, before the checks that read
/// the project. A handler called with no batch open writes at once.
/// </summary>
public static class DeferredWrites
{
    private static readonly ConditionalWeakTable<LcmCache, Batch> Open = new();

    /// <summary>Opens a batch for the cache; disposing it without <see cref="Batch.Complete"/> drops what it held.</summary>
    public static Batch Begin(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var batch = new Batch(cache);
        Open.AddOrUpdate(cache, batch);
        return batch;
    }

    /// <summary>The batch open for the cache, or null.</summary>
    public static Batch? Current(LcmCache cache) => Open.TryGetValue(cache, out var batch) ? batch : null;

    /// <summary>One batch: what to write when it completes and what to forget when it is dropped.</summary>
    public sealed class Batch : IDisposable
    {
        private readonly LcmCache _cache;
        private readonly List<(object Key, Action Write, Action Drop)> _pending = new();
        private bool _done;

        internal Batch(LcmCache cache) => _cache = cache;

        /// <summary>Holds a write back until the batch completes; one key is registered once.</summary>
        public void Hold(object key, Action write, Action drop)
        {
            if (!_pending.Any(each => ReferenceEquals(each.Key, key))) _pending.Add((key, write, drop));
        }

        /// <summary>Makes every held write, in the order they were first held, and closes the batch.</summary>
        public void Complete()
        {
            foreach (var (_, write, _) in _pending) write();
            _pending.Clear();
            Close();
        }

        /// <summary>Drops what is still held and closes the batch.</summary>
        public void Dispose()
        {
            foreach (var (_, _, drop) in _pending) drop();
            _pending.Clear();
            Close();
        }

        private void Close()
        {
            if (_done) return;
            _done = true;
            if (Open.TryGetValue(_cache, out var open) && ReferenceEquals(open, this)) Open.Remove(_cache);
        }
    }
}
