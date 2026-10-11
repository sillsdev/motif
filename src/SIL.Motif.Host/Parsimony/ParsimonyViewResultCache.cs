using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Names one computed view: the proved file pair, the view and its version, and the filters the cursor already
/// binds. A changed file changes the proof key, so a stale result is never served for new bytes.
/// </summary>
internal sealed record ParsimonyViewCacheKey(string ProofKey, string View, int Version, string FilterDigest,
    string? ReportId);

/// <summary>Everything a page needs from one view computation, so later pages only slice it.</summary>
internal sealed record ParsimonyViewResult(IReadOnlyList<ParsimonyViewRow> Rows, string ItemsDigest,
    ParsimonyDispositionProjection? DispositionProjection, ParsimonyMeasureStatus? ComputedStatus,
    string? ComputedDetail);

/// <summary>
/// A small cache of complete view results, bounded by entry count and by total rows, evicting the least
/// recently used. A long-lived host pages through one result; a one-page CLI process never reads it twice.
/// </summary>
internal sealed class ParsimonyViewResultCache(int maximumEntries, int maximumRows)
{
    /// <summary>The cache every view page in this process shares.</summary>
    internal static ParsimonyViewResultCache Shared { get; } = new(16, 50_000);

    private readonly object _gate = new();
    private readonly LinkedList<(ParsimonyViewCacheKey Key, ParsimonyViewResult Result)> _entries = new();
    private int _rows;
    private long _hits;

    /// <summary>Counts every successful lookup that avoided a recomputation.</summary>
    internal long Hits => Interlocked.Read(ref _hits);

    /// <summary>Returns the stored result and marks it most recently used, or null.</summary>
    internal ParsimonyViewResult? Find(ParsimonyViewCacheKey key)
    {
        lock (_gate)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                if (!node.Value.Key.Equals(key)) continue;
                _entries.Remove(node);
                _entries.AddFirst(node);
                _hits++;
                return node.Value.Result;
            }
            return null;
        }
    }

    /// <summary>Stores a result unless it alone exceeds the row bound, then evicts the oldest past the bounds.</summary>
    internal void Store(ParsimonyViewCacheKey key, ParsimonyViewResult result)
    {
        if (result.Rows.Count > maximumRows) return;
        lock (_gate)
        {
            for (var node = _entries.First; node is not null; node = node.Next)
            {
                if (!node.Value.Key.Equals(key)) continue;
                _rows -= node.Value.Result.Rows.Count;
                _entries.Remove(node);
                break;
            }
            _entries.AddFirst((key, result));
            _rows += result.Rows.Count;
            while (_entries.Count > maximumEntries || _rows > maximumRows)
            {
                _rows -= _entries.Last!.Value.Result.Rows.Count;
                _entries.RemoveLast();
            }
        }
    }

    /// <summary>Lists the cached keys from most to least recently used.</summary>
    internal IReadOnlyList<ParsimonyViewCacheKey> Keys()
    {
        lock (_gate) return _entries.Select(entry => entry.Key).ToArray();
    }
}
