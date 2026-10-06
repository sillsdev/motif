namespace SIL.Motif.Worker.Store;

internal readonly record struct RepositoryReadCount(long Queries, long RecordsDeserialized)
{
    public RepositoryReadCount Since(RepositoryReadCount earlier) =>
        new(Queries - earlier.Queries, RecordsDeserialized - earlier.RecordsDeserialized);
}

internal static class RepositoryReadCounters
{
    private static readonly AsyncLocal<RepositoryReadScope?> Current = new();

    public static RepositoryReadScope BeginScope()
    {
        var scope = new RepositoryReadScope(Current.Value);
        Current.Value = scope;
        return scope;
    }

    public static void QueryExecuted() => Current.Value?.QueryExecuted();

    public static void RecordDeserialized() => Current.Value?.RecordDeserialized();

    internal sealed class RepositoryReadScope(RepositoryReadScope? previous) : IDisposable
    {
        private long _queries;
        private long _recordsDeserialized;
        private int _disposed;

        public RepositoryReadCount Snapshot() =>
            new(Interlocked.Read(ref _queries), Interlocked.Read(ref _recordsDeserialized));

        public void QueryExecuted()
        {
            if (Volatile.Read(ref _disposed) == 0) Interlocked.Increment(ref _queries);
        }

        public void RecordDeserialized()
        {
            if (Volatile.Read(ref _disposed) == 0) Interlocked.Increment(ref _recordsDeserialized);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            if (ReferenceEquals(Current.Value, this)) Current.Value = previous;
        }
    }
}
