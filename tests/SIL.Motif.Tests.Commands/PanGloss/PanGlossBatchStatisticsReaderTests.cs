using Microsoft.Data.Sqlite;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

public sealed class PanGlossBatchStatisticsReaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-pangloss-stats-" + Guid.NewGuid().ToString("N"));

    public PanGlossBatchStatisticsReaderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ReadsByIdentityAndKeepsUnsupportedMeasurementsNull()
    {
        var path = Path.Combine(_root, "stats.sqlite");
        WriteCache(path);

        var result = PanGlossBatchStatisticsReader.Read(path, ["word"]);

        Assert.Equal(725_001L, result.Words["word"].ElapsedNs);
        var sameLabel = result.ObjectTimings.Where(row => row.Object == "Shared label").ToArray();
        Assert.Equal(3, sameLabel.Length);
        Assert.Equal(2, sameLabel.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(sameLabel, row => row.Key ==
            "lex_entry:entry-a#00000000-0000-4000-8000-000000000001@Main");
        Assert.Contains(sameLabel, row => row.Key ==
            "lex_entry:entry-b#00000000-0000-4000-8000-000000000002@Main");
        Assert.All(sameLabel, row => Assert.Equal("authored", row.IdentityQuality));
        var unsupported = Assert.Single(sameLabel, row => row.Direction == "synthesis");
        Assert.Null(unsupported.Passes);
        Assert.Null(unsupported.ElapsedNs);
        Assert.Equal(6, unsupported.Attempts);
        Assert.Equal("endo#", Assert.Single(result.ObjectTimings, row => row.Key == "endo#").Object);
    }

    [Fact]
    public void KeepsTheFirstRowsInQueryOrderAndCountsTheRestWhenCapped()
    {
        var path = Path.Combine(_root, "capped.sqlite");
        WriteCache(path);
        var all = PanGlossBatchStatisticsReader.Read(path, ["word"]);
        Assert.False(all.Truncated);
        Assert.True(all.ObjectTimings.Count > 2);

        var capped = PanGlossBatchStatisticsReader.Read(path, ["word"], row => row, 2);

        Assert.True(capped.Truncated);
        Assert.Equal(all.ObjectTimings.Count, capped.TotalObjectTimingRows);
        Assert.Equal(all.ObjectTimings.Take(2), capped.ObjectTimings);
    }

    [Fact]
    public void KeepsTheSlowestWordsAndAggregatesEveryWord()
    {
        var path = Path.Combine(_root, "bounded.sqlite");
        WriteCache(path);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT INTO word VALUES (2, 'slow', 900000, 3, 0); " +
                "INSERT INTO fact VALUES (2, 1, 'analysis', 7, 400000);";
            command.ExecuteNonQuery();
        }

        var result = PanGlossBatchStatisticsReader.ReadBounded(path, ["word", "slow"], 1);

        Assert.Equal(1, result.DetailWordCount);
        Assert.False(result.DetailComplete);
        Assert.All(result.ObjectTimings, row => Assert.Equal("slow", row.Word));
        var aggregate = Assert.Single(result.Aggregates, row => row.Key.Contains("entry-a", StringComparison.Ordinal) &&
            row.Direction == "analysis");
        Assert.Equal(11, aggregate.Attempts);
        Assert.Equal(7, aggregate.MaxAttempts);
        Assert.Equal(580_000, aggregate.ElapsedNs);
        Assert.Equal(2, aggregate.WordsTouched);
    }

    [Fact]
    public void BoundedDetailRanksWorkAboveElapsedTime()
    {
        var path = Path.Combine(_root, "work-ranking.sqlite");
        WriteCache(path);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE word ADD COLUMN work INTEGER; " +
                "UPDATE word SET work = 100 WHERE word_id = 1; " +
                "INSERT INTO word VALUES (2, 'slow', 900000, 3, 0, 1); " +
                "INSERT INTO fact VALUES (2, 1, 'analysis', 7, 400000);";
            command.ExecuteNonQuery();
        }

        var result = PanGlossBatchStatisticsReader.ReadBounded(path, ["word", "slow"], 1);
        Assert.NotEmpty(result.ObjectTimings);
        Assert.All(result.ObjectTimings, row => Assert.Equal("word", row.Word));
    }

    [Fact]
    public void BoundedTotalsCombineAliasesBeforeTakingPerWordMaxima()
    {
        var path = Path.Combine(_root, "aliases.sqlite");
        WriteCache(path);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO object VALUES
                    (4, 'ABCDEFAB-0000-4000-8000-000000000001', 'morph_rule', 'First', 'authored'),
                    (5, 'abcdefab-0000-4000-8000-000000000001', 'morph_rule', 'Second', 'authored');
                INSERT INTO fact VALUES (1, 4, 'analysis', 3, 200), (1, 5, 'analysis', 4, 300);
                """;
            command.ExecuteNonQuery();
        }

        var result = PanGlossBatchStatisticsReader.ReadBounded(path, ["word"], 0);

        Assert.False(result.DetailComplete);
        Assert.Empty(result.ObjectTimings);
        var aggregate = Assert.Single(result.Aggregates, row => row.Key == "abcdefab-0000-4000-8000-000000000001");
        Assert.Equal(7, aggregate.Attempts);
        Assert.Equal(7, aggregate.MaxAttempts);
        Assert.Equal(500, aggregate.MaxElapsedNs);
        Assert.Equal(1, aggregate.WordsTouched);
    }

    [Fact]
    public void ZeroWorkDoesNotCreateRecordedObjectTime()
    {
        var path = Path.Combine(_root, "zero.sqlite");
        WriteCache(path);
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE fact SET attempts = 0, self_time_ns = 0;";
            command.ExecuteNonQuery();
        }

        var result = PanGlossBatchStatisticsReader.ReadBounded(path, ["word"], 0);

        Assert.Null(result.AttributedNs);
        Assert.Null(result.UnattributedNs);
        Assert.All(result.Aggregates, row => Assert.Equal(0, row.WordsTouched));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void RefusesAnUnsupportedCacheVersion()
    {
        var path = Path.Combine(_root, "wrong-version.sqlite");
        WriteCache(path, PanGlossInterfaceVersions.StatsCacheVersion - 1);

        var error = Assert.Throws<InvalidDataException>(() => PanGlossBatchStatisticsReader.Read(path, ["word"]));

        Assert.Contains($"schema version {PanGlossInterfaceVersions.StatsCacheVersion - 1}", error.Message,
            StringComparison.Ordinal);
    }

    private static void WriteCache(string path, int version = PanGlossInterfaceVersions.StatsCacheVersion)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
            CREATE TABLE word (
                word_id INTEGER PRIMARY KEY,
                form TEXT NOT NULL,
                elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL,
                passes INTEGER NOT NULL);
            CREATE TABLE object (
                object_id INTEGER PRIMARY KEY,
                key TEXT NOT NULL,
                kind TEXT NOT NULL,
                label TEXT NOT NULL,
                identity_quality TEXT NOT NULL);
            CREATE TABLE fact (
                word_id INTEGER NOT NULL,
                object_id INTEGER NOT NULL,
                direction TEXT NOT NULL,
                attempts INTEGER NOT NULL,
                self_time_ns INTEGER);
            INSERT INTO cache_identity VALUES (1, $version);
            INSERT INTO word VALUES (1, 'word', 725001, 11, 2);
            INSERT INTO object VALUES
                (1, 'lex_entry:entry-a#00000000-0000-4000-8000-000000000001@Main', 'lex_entry', 'Shared label', 'authored'),
                (2, 'lex_entry:entry-b#00000000-0000-4000-8000-000000000002@Main', 'lex_entry', 'Shared label', 'authored'),
                (3, 'endo#', 'morph_rule', '', 'authored');
            INSERT INTO fact VALUES
                (1, 1, 'analysis', 4, 180000),
                (1, 2, 'analysis', 5, 220000),
                (1, 1, 'synthesis', 6, NULL),
                (1, 3, 'analysis', 2, 1000);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }
}
