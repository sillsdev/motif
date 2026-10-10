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
                (2, 'lex_entry:entry-b#00000000-0000-4000-8000-000000000002@Main', 'lex_entry', 'Shared label', 'authored');
            INSERT INTO fact VALUES
                (1, 1, 'analysis', 4, 180000),
                (1, 2, 'analysis', 5, 220000),
                (1, 1, 'synthesis', 6, NULL);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }
}
