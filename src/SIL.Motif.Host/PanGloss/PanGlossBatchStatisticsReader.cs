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
        command.CommandText = "SELECT form, attempts, passes FROM word ORDER BY word_id";
        using var reader = command.ExecuteReader();
        var words = new Dictionary<string, PanGlossWordStatistics>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var word = reader.GetString(0);
            if (string.IsNullOrWhiteSpace(word) || !words.TryAdd(word, new PanGlossWordStatistics(
                    ReadCounter(reader, 1, "attempts", word), ReadCounter(reader, 2, "passes", word))))
                throw new InvalidDataException("The batch statistics cache contains an empty or duplicate word row.");
        }
        return words;
    }

    private static IReadOnlyList<PanGlossObjectTiming> ReadObjectTimings(
        SqliteConnection connection, IReadOnlyDictionary<string, PanGlossWordStatistics> words)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT w.form, o.kind, o.label, SUM(f.attempts), SUM(f.self_time_ns)
            FROM fact AS f
            JOIN word AS w ON w.word_id = f.word_id
            JOIN object AS o ON o.object_id = f.object_id
            GROUP BY w.form, o.object_id, o.kind, o.label
            ORDER BY w.form, o.kind, o.label;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<PanGlossObjectTiming>();
        while (reader.Read())
        {
            var word = reader.GetString(0);
            var kind = reader.GetString(1);
            var label = reader.GetString(2);
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(label) || !words.TryGetValue(word, out var stats))
                throw new InvalidDataException("The batch statistics cache contains an invalid object fact row.");
            var attempts = ReadCounter(reader, 3, "object attempts", word);
            var nanoseconds = reader.GetInt64(4);
            if (nanoseconds < 0)
                throw new InvalidDataException($"The batch statistics cache has negative object time for '{word}'.");
            rows.Add(new PanGlossObjectTiming(kind, label, word, attempts, stats.Passes,
                nanoseconds / 1_000_000d));
        }
        return rows;
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
/// <param name="Words">Attempts and passes, indexed by each exact form in the Selection.</param>
/// <param name="ObjectTimings">The per-word timings aggregated by PanGloss object identity.</param>
public sealed record PanGlossBatchStatistics(
    IReadOnlyDictionary<string, PanGlossWordStatistics> Words,
    IReadOnlyList<PanGlossObjectTiming> ObjectTimings);

/// <summary>PanGloss's whole-word attempt and pass counters for a batch result.</summary>
/// <param name="Attempts">The search steps recorded for the word.</param>
/// <param name="Passes">The analyses returned for the word.</param>
public sealed record PanGlossWordStatistics(int Attempts, int Passes);
