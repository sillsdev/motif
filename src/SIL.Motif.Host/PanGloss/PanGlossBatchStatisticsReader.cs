using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads word counters and per-object timings from one retained PanGloss batch statistics cache.</summary>
public static class PanGlossBatchStatisticsReader
{
    /// <summary>The most object timing rows kept from one cache, so a very large project cannot exhaust memory.</summary>
    public const int DefaultMaxObjectTimingRows = 1_000_000;

    /// <summary>Reads the batch rows and validates exact Selection coverage in the cache.</summary>
    /// <param name="cachePath">The statistics cache written by the batch invocation.</param>
    /// <param name="expectedWords">The exact resolved Selection measured by that invocation.</param>
    /// <returns>The per-word counters and per-object timings stored in the cache.</returns>
    /// <exception cref="InvalidDataException">The cache does not contain exactly the requested word rows.</exception>
    public static PanGlossBatchStatistics<PanGlossObjectTiming> Read(
        string cachePath, IReadOnlyCollection<string> expectedWords) =>
        Read(cachePath, expectedWords, timing => timing, DefaultMaxObjectTimingRows);

    /// <summary>
    /// Reads the batch rows, converting each object timing as it is read so only one list is held, and keeping
    /// at most <paramref name="maxObjectTimingRows"/> of them in the query's own order.
    /// </summary>
    /// <param name="cachePath">The statistics cache written by the batch invocation.</param>
    /// <param name="expectedWords">The exact resolved Selection measured by that invocation.</param>
    /// <param name="convert">Maps each validated timing row to the caller's own shape.</param>
    /// <param name="maxObjectTimingRows">The row cap; rows past it are counted but not kept.</param>
    /// <returns>
    /// The per-word counters, the kept converted timings, and the total number of timing rows in the cache.
    /// </returns>
    /// <exception cref="InvalidDataException">The cache does not contain exactly the requested word rows.</exception>
    public static PanGlossBatchStatistics<T> Read<T>(
        string cachePath, IReadOnlyCollection<string> expectedWords,
        Func<PanGlossObjectTiming, T> convert, int maxObjectTimingRows)
    {
        ArgumentNullException.ThrowIfNull(convert);
        ArgumentOutOfRangeException.ThrowIfNegative(maxObjectTimingRows);
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(expectedWords);
        var expected = expectedWords.ToHashSet(StringComparer.Ordinal);
        if (expected.Count != expectedWords.Count)
            throw new ArgumentException("The resolved Selection must not contain duplicate words.", nameof(expectedWords));
        if (!File.Exists(cachePath)) throw new FileNotFoundException("The PanGloss batch statistics cache is missing.", cachePath);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(cachePath),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();

        var schemaVersion = ReadSchemaVersion(connection);
        if (schemaVersion != PanGlossInterfaceVersions.StatsCacheVersion)
            throw new InvalidDataException(
                $"The PanGloss batch statistics cache has schema version {schemaVersion}; " +
                $"Motif requires version {PanGlossInterfaceVersions.StatsCacheVersion}.");

        var words = ReadWords(connection);
        var unexpected = words.Keys.FirstOrDefault(word => !expected.Contains(word));
        if (unexpected is not null)
            throw new InvalidDataException($"The batch statistics cache contains unrequested word '{unexpected}'.");
        var missing = expected.FirstOrDefault(word => !words.ContainsKey(word));
        if (missing is not null)
            throw new InvalidDataException($"The batch statistics cache has no per-word row for selected word '{missing}'.");

        var (timings, total) = ReadObjectTimings(connection, words, convert, maxObjectTimingRows);
        return new PanGlossBatchStatistics<T>(words, timings, total);
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT schema_version FROM cache_identity WHERE cache_id = 1";
        object? value;
        try { value = command.ExecuteScalar(); }
        catch (SqliteException exception)
        {
            throw new InvalidDataException("The PanGloss batch statistics cache has no valid cache identity.", exception);
        }
        if (value is long version && version is >= 0 and <= int.MaxValue) return (int)version;
        throw new InvalidDataException("The PanGloss batch statistics cache has no valid cache identity.");
    }

    // The word table's work column is read only where the cache declares it, so a cache without one still reads.
    private static bool WordTableHasWork(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('word') WHERE name = 'work'";
        return (long)(command.ExecuteScalar() ?? 0L) > 0;
    }

    private static Dictionary<string, PanGlossWordStatistics> ReadWords(SqliteConnection connection)
    {
        var hasWork = WordTableHasWork(connection);
        using var command = connection.CreateCommand();
        command.CommandText = hasWork
            ? "SELECT form, elapsed_ns, attempts, passes, work FROM word ORDER BY word_id"
            : "SELECT form, elapsed_ns, attempts, passes FROM word ORDER BY word_id";
        using var reader = command.ExecuteReader();
        var words = new Dictionary<string, PanGlossWordStatistics>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var word = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(word) || !words.TryAdd(word, new PanGlossWordStatistics(
                    ReadCounter(reader, 2, "attempts", word), ReadCounter(reader, 3, "passes", word),
                    ReadElapsedNs(reader, 1, "word", word))
                {
                    WorkSteps = hasWork && !reader.IsDBNull(4) ? ReadWork(reader, 4, word) : null,
                }))
                throw new InvalidDataException("The batch statistics cache contains an empty or duplicate word row.");
        }
        return words;
    }

    internal const string ObjectTimingsFrom = """
        FROM fact AS f
        JOIN word AS w ON w.word_id = f.word_id
        JOIN object AS o ON o.object_id = f.object_id
        GROUP BY w.form, o.key, o.identity_quality, o.kind, o.label, f.direction
        """;

    internal const string ObjectTimingsQuery = """
        SELECT w.form, o.key, o.identity_quality, o.kind, o.label, f.direction,
               SUM(f.attempts), SUM(f.self_time_ns)
        """ + "\n" + ObjectTimingsFrom + """

        ORDER BY w.form, o.kind, o.key, f.direction;
        """;

    private static (IReadOnlyList<T> Rows, long Total) ReadObjectTimings<T>(
        SqliteConnection connection, IReadOnlyDictionary<string, PanGlossWordStatistics> words,
        Func<PanGlossObjectTiming, T> convert, int maxRows)
    {
        using var command = connection.CreateCommand();
        command.CommandText = ObjectTimingsQuery;
        using var reader = command.ExecuteReader();
        var rows = new List<T>();
        while (reader.Read())
        {
            if (rows.Count >= maxRows)
            {
                // Counted by SQLite without materializing, so memory stays bounded by the cap.
                reader.Close();
                return (rows, CountObjectTimings(connection));
            }
            var word = reader.GetString(0);
            var key = reader.GetString(1);
            var identityQuality = reader.GetString(2);
            var kind = reader.GetString(3);
            var label = reader.GetString(4);
            var direction = reader.GetString(5);
            if (string.IsNullOrWhiteSpace(key) ||
                !IsKnownIdentityQuality(identityQuality) || !IsKnownKind(kind) || !IsKnownDirection(direction) ||
                !words.ContainsKey(word))
                throw new InvalidDataException("The batch statistics cache contains an invalid object fact row.");
            if (string.IsNullOrWhiteSpace(label)) label = key;
            int? attempts = SupportsCounter(kind, "attempts") ? ReadCounter(reader, 6, "object attempts", word) : null;
            long? nanoseconds = SupportsTimer(kind, direction)
                ? ReadElapsedNs(reader, 7, "object", word) : null;
            rows.Add(convert(new PanGlossObjectTiming(kind, key, identityQuality, direction, label, word, attempts,
                null, nanoseconds)));
        }
        return (rows, rows.Count);
    }

    private static long CountObjectTimings(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM (SELECT 1 " + ObjectTimingsFrom + ")";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    internal static bool IsKnownKind(string kind) => kind is
        "morph_rule" or "phon_rule" or "lex_entry" or "root_index" or "guesser" or "overlay";

    internal static bool IsKnownIdentityQuality(string identityQuality) => identityQuality is
        "authored" or "structural" or "synthetic";

    internal static bool IsKnownDirection(string direction) => direction is "analysis" or "synthesis";

    internal static bool SupportsCounter(string kind, string counter) => counter == "attempts" && IsKnownKind(kind);

    internal static bool SupportsTimer(string kind, string direction) => kind switch
    {
        "morph_rule" or "phon_rule" => true,
        "lex_entry" or "root_index" or "guesser" or "overlay" => direction == "analysis",
        _ => false,
    };

    private static long ReadElapsedNs(SqliteDataReader reader, int ordinal, string scope, string word)
    {
        var value = reader.GetInt64(ordinal);
        if (value < 0)
            throw new InvalidDataException($"The batch statistics cache has negative {scope} time for '{word}'.");
        return value;
    }

    private static long ReadWork(SqliteDataReader reader, int ordinal, string word)
    {
        var value = reader.GetInt64(ordinal);
        if (value < 0)
            throw new InvalidDataException($"The batch statistics cache has a negative work count for '{word}'.");
        return value;
    }

    private static int ReadCounter(SqliteDataReader reader, int ordinal, string name, string word)
    {
        var value = reader.GetInt64(ordinal);
        if (value is < 0 or > int.MaxValue)
            throw new InvalidDataException($"The batch statistics cache has an invalid {name} count for '{word}'.");
        return (int)value;
    }
}

/// <summary>The per-word counters and object timings recorded by one PanGloss batch run.</summary>
/// <param name="Words">Attempts, passes and elapsed nanoseconds, indexed by each exact form in the Selection.</param>
/// <param name="ObjectTimings">The per-word timings aggregated by PanGloss object identity, up to the cap.</param>
/// <param name="TotalObjectTimingRows">How many timing rows the cache holds, including any past the cap.</param>
/// <typeparam name="T">The shape each timing row was converted to as it was read.</typeparam>
public sealed record PanGlossBatchStatistics<T>(
    IReadOnlyDictionary<string, PanGlossWordStatistics> Words,
    IReadOnlyList<T> ObjectTimings,
    long TotalObjectTimingRows)
{
    /// <summary>True when rows past the cap were counted but not kept.</summary>
    public bool Truncated => TotalObjectTimingRows > ObjectTimings.Count;
}

/// <summary>PanGloss's whole-word counters and elapsed time for a batch result.</summary>
/// <param name="Attempts">The analysis attempts recorded for the word.</param>
/// <param name="Passes">The analyses returned for the word.</param>
/// <param name="ElapsedNs">The exact whole-word elapsed time recorded by PanGloss.</param>
public sealed record PanGlossWordStatistics(int Attempts, int Passes, long ElapsedNs)
{
    /// <summary>The total work PanGloss recorded for the word, or null when the cache has no such column.</summary>
    public long? WorkSteps { get; init; }
}
