using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParsimonySqlitePathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-sqlite-path-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void ArtifactBeyondTheLegacyPathLimitOpensForWritingAndAttachesReadOnly()
    {
        var directory = Path.Combine(_root, new string('a', 100), new string('b', 100), new string('c', 40));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "evidence.sqlite");
        Assert.True(path.Length > 300, $"The probe path is only {path.Length} characters.");

        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = ParsimonySqlitePath.DataSource(path), Pooling = false,
               }.ToString()))
        {
            writer.Open();
            using var create = writer.CreateCommand();
            create.CommandText = "CREATE TABLE probe(value INTEGER); INSERT INTO probe VALUES (7);";
            create.ExecuteNonQuery();
        }

        using var session = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = ":memory:", Mode = SqliteOpenMode.Memory, Pooling = false,
        }.ToString());
        session.Open();
        using var command = session.CreateCommand();
        command.CommandText = "ATTACH DATABASE $path AS evidence;";
        command.Parameters.AddWithValue("$path", ParsimonySqlitePath.ReadOnlyUri(path));
        command.ExecuteNonQuery();
        command.Parameters.Clear();
        command.CommandText = "SELECT value FROM evidence.probe;";
        Assert.Equal(7L, command.ExecuteScalar());
        command.CommandText = "CREATE TABLE evidence.written(value INTEGER);";
        var refused = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        Assert.Equal(8, refused.SqliteErrorCode);
    }

    [Fact]
    public void FactsIntegrityCheckOpensAFileBeyondTheLegacyPathLimit()
    {
        var directory = Path.Combine(_root, new string('a', 100), new string('b', 100), new string('c', 40));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "grammar-facts.sqlite");
        Assert.True(path.Length > 300, $"The probe path is only {path.Length} characters.");
        using (var writer = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = ParsimonySqlitePath.DataSource(path), Pooling = false,
               }.ToString()))
        {
            writer.Open();
            using var create = writer.CreateCommand();
            create.CommandText = "CREATE TABLE probe(value INTEGER);";
            create.ExecuteNonQuery();
        }

        ParsimonyFactsIntegrity.Verify(path);
    }
}
