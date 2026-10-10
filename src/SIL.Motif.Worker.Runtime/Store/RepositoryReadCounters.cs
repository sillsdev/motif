namespace SIL.Motif.Worker.Store;

internal readonly record struct RepositoryReadCount(long Queries, long RecordsDeserialized,
    long BaselineJsonPayloadBytes = 0, long BaselineOccurrenceTuples = 0)
{
    public RepositoryReadCount Since(RepositoryReadCount earlier) =>
        new(Queries - earlier.Queries, RecordsDeserialized - earlier.RecordsDeserialized,
            BaselineJsonPayloadBytes - earlier.BaselineJsonPayloadBytes,
            BaselineOccurrenceTuples - earlier.BaselineOccurrenceTuples);
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

    public static void QueryExecuted()
    {
        for (var scope = Current.Value; scope is not null; scope = scope.Previous)
            scope.QueryExecuted();
    }

    public static void RecordDeserialized(long baselineJsonPayloadBytes = 0, long baselineOccurrenceTuples = 0)
    {
        for (var scope = Current.Value; scope is not null; scope = scope.Previous)
            scope.RecordDeserialized(baselineJsonPayloadBytes, baselineOccurrenceTuples);
    }

    internal sealed class RepositoryReadScope : IDisposable
    {
        internal RepositoryReadScope? Previous { get; }
        private long _queries;
        private long _recordsDeserialized;
        private long _baselineJsonPayloadBytes;
        private long _baselineOccurrenceTuples;
        private int _disposed;

        public RepositoryReadScope(RepositoryReadScope? previous) => Previous = previous;

        public RepositoryReadCount Snapshot() =>
            new(Interlocked.Read(ref _queries), Interlocked.Read(ref _recordsDeserialized),
                Interlocked.Read(ref _baselineJsonPayloadBytes), Interlocked.Read(ref _baselineOccurrenceTuples));

        public void QueryExecuted()
        {
            if (Volatile.Read(ref _disposed) == 0) Interlocked.Increment(ref _queries);
        }

        public void RecordDeserialized(long baselineJsonPayloadBytes, long baselineOccurrenceTuples)
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Interlocked.Increment(ref _recordsDeserialized);
            Interlocked.Add(ref _baselineJsonPayloadBytes, baselineJsonPayloadBytes);
            Interlocked.Add(ref _baselineOccurrenceTuples, baselineOccurrenceTuples);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _disposed, 1);
            if (ReferenceEquals(Current.Value, this)) Current.Value = Previous;
        }
    }
}
