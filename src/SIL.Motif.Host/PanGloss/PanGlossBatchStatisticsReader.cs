using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Responses;

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

    /// <summary>Reads exact object totals while keeping detail for the words with the most work.</summary>
    /// <param name="cachePath">The PanGloss statistics cache.</param>
    /// <param name="expectedWords">The exact Selection measured by this invocation.</param>
    /// <param name="detailWordLimit">Maximum number of words whose object facts are retained.</param>
    /// <returns>Exact per-object totals and the bounded word detail.</returns>
    public static BoundedPanGlossStatistics ReadBounded(string cachePath,
        IReadOnlyCollection<string> expectedWords, int detailWordLimit)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(detailWordLimit);
        ArgumentException.ThrowIfNullOrWhiteSpace(cachePath);
        ArgumentNullException.ThrowIfNull(expectedWords);
        var expected = expectedWords.ToHashSet(StringComparer.Ordinal);
        if (expected.Count != expectedWords.Count)
            throw new ArgumentException("The resolved Selection must not contain duplicate words.", nameof(expectedWords));
        if (!File.Exists(cachePath)) throw new FileNotFoundException("The PanGloss batch statistics cache is missing.", cachePath);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(cachePath), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        connection.Open();
        var version = ReadSchemaVersion(connection);
        if (version != PanGlossInterfaceVersions.StatsCacheVersion)
            throw new InvalidDataException($"The PanGloss batch statistics cache has schema version {version}; " +
                $"Motif requires version {PanGlossInterfaceVersions.StatsCacheVersion}.");
        var words = ReadWords(connection);
        var unexpected = words.Keys.FirstOrDefault(word => !expected.Contains(word));
        if (unexpected is not null)
            throw new InvalidDataException($"The batch statistics cache contains unrequested word '{unexpected}'.");
        var missing = expected.FirstOrDefault(word => !words.ContainsKey(word));
        if (missing is not null)
            throw new InvalidDataException($"The batch statistics cache has no per-word row for selected word '{missing}'.");
        var selected = words.OrderByDescending(pair => pair.Value.WorkSteps.HasValue)
            .ThenByDescending(pair => pair.Value.WorkSteps)
            .ThenByDescending(pair => pair.Value.WorkSteps is null ? pair.Value.ElapsedNs : 0)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(detailWordLimit)
            .Select(pair => pair.Key).ToHashSet(StringComparer.Ordinal);
        var detail = new List<PanGlossObjectTiming>();
        var attributedByWord = new Dictionary<string, long>(StringComparer.Ordinal);
        var hasTimer = false;
        var aggregates = new Dictionary<(ObjectIdentity Identity, string Direction), PanGlossObjectTimingAggregate>();
        var directionWords = new Dictionary<(ObjectIdentity Identity, string Direction), string>();
        var directionWordTotals = new Dictionary<(ObjectIdentity Identity, string Direction), (string Word, long Attempts, long Elapsed)>();
        var objectWords = new Dictionary<ObjectIdentity, (string Word, int Count)>();
        var kindWords = new Dictionary<string, (string Word, int Count)>(StringComparer.Ordinal);
        long total = 0;
        using var command = connection.CreateCommand();
        command.CommandText = ObjectTimingsQuery;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var row = ReadTiming(reader, words);
            total = checked(total + 1);
            if (selected.Contains(row.Word)) detail.Add(row);
            var touched = row.Attempts is > 0 || row.ElapsedNs is > 0;
            if (touched && row.ElapsedNs is { } duration)
            {
                hasTimer = true;
                attributedByWord[row.Word] = checked(attributedByWord.GetValueOrDefault(row.Word) + duration);
            }
            var identity = ObjectIdentity.Create(row.Kind, row.Key, row.IdentityQuality)
                ?? throw new InvalidDataException("The batch statistics cache contains an unscoped object identity.");
            var key = (identity, row.Direction);
            if (!aggregates.TryGetValue(key, out var prior))
                prior = new(row.Kind, row.Key, row.IdentityQuality, row.Direction, row.Object, 0, null, null, null, null);
            if (touched)
            {
                var priorObject = objectWords.GetValueOrDefault(identity);
                if (priorObject.Word != row.Word) objectWords[identity] = (row.Word, checked(priorObject.Count + 1));
                var priorKind = kindWords.GetValueOrDefault(row.Kind);
                if (priorKind.Word != row.Word) kindWords[row.Kind] = (row.Word, checked(priorKind.Count + 1));
            }
            var newDirectionWord = touched && directionWords.GetValueOrDefault(key) != row.Word;
            if (newDirectionWord) directionWords[key] = row.Word;
            var wordTotals = directionWordTotals.GetValueOrDefault(key);
            if (wordTotals.Word != row.Word) wordTotals = (row.Word, 0, 0);
            wordTotals.Attempts = checked(wordTotals.Attempts + row.Attempts.GetValueOrDefault());
            wordTotals.Elapsed = checked(wordTotals.Elapsed + row.ElapsedNs.GetValueOrDefault());
            directionWordTotals[key] = wordTotals;
            aggregates[key] = prior with
            {
                Object = string.CompareOrdinal(row.Object, prior.Object) < 0 ? row.Object : prior.Object,
                WordsTouched = checked(prior.WordsTouched + (newDirectionWord ? 1 : 0)),
                Attempts = row.Attempts is { } attempts ? checked((prior.Attempts ?? 0) + attempts) : prior.Attempts,
                MaxAttempts = row.Attempts is not null ? Math.Max(prior.MaxAttempts ?? 0, wordTotals.Attempts) : prior.MaxAttempts,
                ElapsedNs = row.ElapsedNs is { } elapsed ? checked((prior.ElapsedNs ?? 0) + elapsed) : prior.ElapsedNs,
                MaxElapsedNs = row.ElapsedNs is not null ? Math.Max(prior.MaxElapsedNs ?? 0, wordTotals.Elapsed) : prior.MaxElapsedNs,
            };
        }
        var completed = aggregates.Select(pair => pair.Value with
        {
            Key = pair.Key.Identity.Key,
            WordsTouchedAcrossDirections = objectWords.GetValueOrDefault(pair.Key.Identity).Count,
            WordsTouchedForKind = kindWords.GetValueOrDefault(pair.Value.Kind).Count,
        }).ToArray();
        long attributed = 0, unattributed = 0, overrun = 0;
        foreach (var (word, stats) in words)
        {
            var objectTime = attributedByWord.GetValueOrDefault(word);
            attributed = checked(attributed + objectTime);
            var residual = stats.ElapsedNs - objectTime;
            if (residual >= 0) unattributed = checked(unattributed + residual);
            else overrun = checked(overrun - residual);
        }
        return new(words, detail, completed, selected.Count, selected.Count == words.Count, total)
        {
            AttributedNs = hasTimer ? attributed : null,
            UnattributedNs = hasTimer ? unattributed : null,
            OverrunNs = hasTimer ? overrun : null,
        };
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

    private const string ObjectTimingsQuery =
        "SELECT w.form, o.key, o.identity_quality, o.kind, o.label, f.direction, " +
        "SUM(f.attempts), SUM(f.self_time_ns), SUM(f.work_steps)\n" + ObjectTimingsFrom +
        "\nORDER BY w.form, o.kind, o.key, f.direction;";

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
            rows.Add(convert(ReadTiming(reader, words)));
        }
        return (rows, rows.Count);
    }

    private static PanGlossObjectTiming ReadTiming(SqliteDataReader reader,
        IReadOnlyDictionary<string, PanGlossWordStatistics> words)
    {
        var word = reader.GetString(0);
        var key = reader.GetString(1);
        var identityQuality = reader.GetString(2);
        var kind = reader.GetString(3);
        var label = reader.GetString(4);
        var direction = reader.GetString(5);
        var unattributed = kind == "unattributed" && key == "unattributed" &&
            identityQuality == "synthetic" && direction == "unattributed";
        if (string.IsNullOrWhiteSpace(key) || !IsKnownIdentityQuality(identityQuality) ||
            (!unattributed && (!IsKnownKind(kind) || !IsKnownDirection(direction))) || !words.ContainsKey(word))
            throw new InvalidDataException("The batch statistics cache contains an invalid object fact row.");
        if (string.IsNullOrWhiteSpace(label)) label = key;
        int? attempts = SupportsCounter(kind, "attempts") ? ReadCounter(reader, 6, "object attempts", word) : null;
        long? nanoseconds = SupportsTimer(kind, direction) ? ReadElapsedNs(reader, 7, "object", word) : null;
        return new PanGlossObjectTiming(kind, key, identityQuality, direction, label, word, attempts, null, nanoseconds)
        { WorkSteps = reader.IsDBNull(8) ? null : ReadWork(reader, 8, word) };
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

/// <summary>Exact totals for an object and direction across one complete batch.</summary>
public sealed record PanGlossObjectTimingAggregate(string Kind, string Key, string IdentityQuality, string Direction,
    string Object, int WordsTouched, long? Attempts, long? MaxAttempts, long? ElapsedNs, long? MaxElapsedNs)
{
    /// <summary>Distinct words where this object did work in either direction.</summary>
    public int WordsTouchedAcrossDirections { get; init; }
    /// <summary>Distinct words where any object of this kind did work.</summary>
    public int WordsTouchedForKind { get; init; }
}

/// <summary>Exact batch totals and the selected words' object facts.</summary>
public sealed record BoundedPanGlossStatistics(
    IReadOnlyDictionary<string, PanGlossWordStatistics> Words,
    IReadOnlyList<PanGlossObjectTiming> ObjectTimings,
    IReadOnlyList<PanGlossObjectTimingAggregate> Aggregates,
    int DetailWordCount, bool DetailComplete, long TotalObjectTimingRows)
{
    /// <summary>Total object time for all measured words, when any timer was recorded.</summary>
    public long? AttributedNs { get; init; }
    /// <summary>Sum of each word's nonnegative residual outside object timers.</summary>
    public long? UnattributedNs { get; init; }
    /// <summary>Sum of object time exceeding each word's total.</summary>
    public long? OverrunNs { get; init; }
}
