using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Reads word counters and per-object timings from one retained PanGloss batch statistics cache.</summary>
public static class PanGlossBatchStatisticsReader
{
    /// <summary>Reads the batch rows and validates exact Selection coverage in the cache.</summary>
    /// <param name="cachePath">The statistics cache written by the batch invocation.</param>
    /// <param name="expectedWords">The exact resolved Selection measured by that invocation.</param>
    /// <returns>The per-word counters and per-object timings stored in the cache.</returns>
    /// <exception cref="InvalidDataException">The cache does not contain exactly the requested word rows.</exception>
    public static PanGlossBatchStatistics Read(string cachePath, IReadOnlyCollection<string> expectedWords)
    {
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

        var words = ReadWords(connection);
        var unexpected = words.Keys.FirstOrDefault(word => !expected.Contains(word));
        if (unexpected is not null)
            throw new InvalidDataException($"The batch statistics cache contains unrequested word '{unexpected}'.");
        var missing = expected.FirstOrDefault(word => !words.ContainsKey(word));
        if (missing is not null)
            throw new InvalidDataException($"The batch statistics cache has no per-word row for selected word '{missing}'.");

        return new PanGlossBatchStatistics(words, ReadObjectTimings(connection, words));
    }

    private static Dictionary<string, PanGlossWordStatistics> ReadWords(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT form, elapsed_ns, attempts, passes FROM word ORDER BY word_id";
        using var reader = command.ExecuteReader();
        var words = new Dictionary<string, PanGlossWordStatistics>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var word = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(word) || !words.TryAdd(word, new PanGlossWordStatistics(
                    ReadCounter(reader, 2, "attempts", word), ReadCounter(reader, 3, "passes", word),
                    ReadElapsedNs(reader, 1, "word", word))))
                throw new InvalidDataException("The batch statistics cache contains an empty or duplicate word row.");
        }
        return words;
    }

    private static IReadOnlyList<PanGlossObjectTiming> ReadObjectTimings(
        SqliteConnection connection, IReadOnlyDictionary<string, PanGlossWordStatistics> words)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.form, o.key, o.identity_quality, o.kind, o.label, f.direction,
                   SUM(f.attempts), SUM(f.self_time_ns)
            FROM fact AS f
            JOIN word AS w ON w.word_id = f.word_id
            JOIN object AS o ON o.object_id = f.object_id
            GROUP BY w.form, o.key, o.identity_quality, o.kind, o.label, f.direction
            ORDER BY w.form, o.kind, o.key, f.direction;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<PanGlossObjectTiming>();
        while (reader.Read())
        {
            var word = reader.GetString(0);
            var key = reader.GetString(1);
            var identityQuality = reader.GetString(2);
            var kind = reader.GetString(3);
            var label = reader.GetString(4);
            var direction = reader.GetString(5);
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(label) ||
                !IsKnownIdentityQuality(identityQuality) || !IsKnownKind(kind) || !IsKnownDirection(direction) ||
                !words.ContainsKey(word))
                throw new InvalidDataException("The batch statistics cache contains an invalid object fact row.");
            int? attempts = SupportsCounter(kind, "attempts") ? ReadCounter(reader, 6, "object attempts", word) : null;
            long? nanoseconds = SupportsTimer(kind, direction)
                ? ReadElapsedNs(reader, 7, "object", word) : null;
            rows.Add(new PanGlossObjectTiming(kind, key, identityQuality, direction, label, word, attempts, null,
                nanoseconds));
        }
        return rows;
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
/// <param name="ObjectTimings">The per-word timings aggregated by PanGloss object identity.</param>
public sealed record PanGlossBatchStatistics(
    IReadOnlyDictionary<string, PanGlossWordStatistics> Words,
    IReadOnlyList<PanGlossObjectTiming> ObjectTimings);

/// <summary>PanGloss's whole-word counters and elapsed time for a batch result.</summary>
/// <param name="Attempts">The search steps recorded for the word.</param>
/// <param name="Passes">The analyses returned for the word.</param>
/// <param name="ElapsedNs">The exact whole-word elapsed time recorded by PanGloss.</param>
public sealed record PanGlossWordStatistics(int Attempts, int Passes, long ElapsedNs);
