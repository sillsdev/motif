using System.Collections.Immutable;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>Counts every row a read takes from a projection, so a test can prove a lookup never scans.</summary>
internal sealed class RowCounter
{
    private long value;

    public long Value => Interlocked.Read(ref value);

    public void Add(long rows) => Interlocked.Add(ref value, rows);
}

/// <summary>
/// The rows of a projection behind an indexer that counts each access. It cannot be enumerated, so a lookup that walks
/// the rows has to read them one by one and the counter sees it.
/// </summary>
internal sealed class CountedRows<T>(ImmutableArray<T> rows, RowCounter counter)
{
    public int Length => rows.Length;

    public T this[int index]
    {
        get
        {
            counter.Add(1);
            return rows[index];
        }
    }
}
