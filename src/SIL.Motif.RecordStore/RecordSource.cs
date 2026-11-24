using System.Collections.Immutable;

namespace SIL.Motif.RecordStore;

/// <summary>
/// The native records of one verified point. A fully decoded point holds them all; a certified point holds none until asked:
/// <see cref="GetAsync"/> reads and checks the one chunk a record lies in, and <see cref="AllAsync"/> decodes and checks the whole store.
/// </summary>
internal sealed class RecordSource
{
    private readonly SemaphoreSlim loading = new(1, 1);
    private readonly Func<StoreState, CancellationToken, Task<Dictionary<Guid, GenericRecord>>>? readAll;
    private readonly Func<Guid, StoreState, CancellationToken, Task<GenericRecord?>>? readOne;
    private readonly Func<IReadOnlyList<Guid>, StoreState, CancellationToken, Task<GenericRecord?[]>>? readMany;
    // The published point the keyed reads must still be looking at; a write moves it with With.
    private readonly StoreState? expected;
    private readonly ImmutableDictionary<Guid, GenericRecord> overlay;
    private volatile Dictionary<Guid, GenericRecord>? all;

    private RecordSource(Dictionary<Guid, GenericRecord>? all, Func<StoreState, CancellationToken, Task<Dictionary<Guid, GenericRecord>>>? readAll,
        Func<Guid, StoreState, CancellationToken, Task<GenericRecord?>>? readOne,
        Func<IReadOnlyList<Guid>, StoreState, CancellationToken, Task<GenericRecord?[]>>? readMany, StoreState? expected,
        ImmutableDictionary<Guid, GenericRecord> overlay)
    {
        this.all = all; this.readAll = readAll; this.readOne = readOne; this.readMany = readMany; this.expected = expected; this.overlay = overlay;
    }

    public static RecordSource Eager(Dictionary<Guid, GenericRecord> records) =>
        new(records, null, null, null, null, ImmutableDictionary<Guid, GenericRecord>.Empty);

    public static RecordSource Lazy(StoreState expected, Func<Guid, StoreState, CancellationToken, Task<GenericRecord?>> readOne,
        Func<IReadOnlyList<Guid>, StoreState, CancellationToken, Task<GenericRecord?[]>> readMany,
        Func<StoreState, CancellationToken, Task<Dictionary<Guid, GenericRecord>>> readAll) =>
        new(null, readAll, readOne, readMany, expected, ImmutableDictionary<Guid, GenericRecord>.Empty);

    /// <summary>False while this source can still answer by keyed reads only.</summary>
    public bool IsLoaded => all is not null;

    /// <summary>The records, once <see cref="AllAsync"/> has completed (always, for a fully decoded point).</summary>
    public IReadOnlyDictionary<Guid, GenericRecord> Loaded =>
        all is { } records ? records : throw new InvalidOperationException("The point's records have not been loaded.");

    public async Task<GenericRecord?> GetAsync(Guid id, CancellationToken ct)
    {
        if (overlay.TryGetValue(id, out var updated)) return updated;
        if (all is { } records) return records.GetValueOrDefault(id);
        return await readOne!(id, expected!, ct);
    }

    /// <summary>Several records in one read of the point, in the order asked; a missing record is null.</summary>
    public async Task<GenericRecord?[]> GetManyAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        var found = new GenericRecord?[ids.Count];
        var missing = new List<int>();
        for (var i = 0; i < ids.Count; i++)
        {
            if (overlay.TryGetValue(ids[i], out var updated)) found[i] = updated;
            else if (all is { } records) found[i] = records.GetValueOrDefault(ids[i]);
            else missing.Add(i);
        }
        if (missing.Count == 0) return found;
        var read = await readMany!(missing.Select(i => ids[i]).ToArray(), expected!, ct);
        for (var n = 0; n < missing.Count; n++) found[missing[n]] = read[n];
        return found;
    }

    public async Task<IReadOnlyDictionary<Guid, GenericRecord>> AllAsync(CancellationToken ct)
    {
        if (all is { } loaded) return loaded;
        await loading.WaitAsync(ct);
        try
        {
            if (all is { } again) return again;
            var records = await readAll!(expected!, ct);
            foreach (var (id, record) in overlay) records[id] = record;
            return all = records;
        }
        finally { loading.Release(); }
    }

    /// <summary>The same records with one replaced; a decoded point copies its dictionary, a lazy one keeps its keyed reads.</summary>
    public RecordSource With(GenericRecord updated, StoreState published)
    {
        if (all is { } records) return Eager(new Dictionary<Guid, GenericRecord>(records) { [updated.Id] = updated });
        return new(null, readAll, readOne, readMany, published, overlay.SetItem(updated.Id, updated));
    }
}
