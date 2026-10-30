using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>Covers what a person is told when their build cannot open the database in front of it.</summary>
/// <remarks>
/// The CLI and the job runner both open this database, so an upgrade race puts an ordinary user in front
/// of this refusal. What it says therefore has to be actionable rather than merely accurate.
/// </remarks>
public sealed class SchemaVersionGateTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-gate-" + Guid.NewGuid().ToString("N"));

    public SchemaVersionGateTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void NewProjectStoresUseIncrementalVacuum()
    {
        var path = Path.Combine(_root, "incremental.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "incremental.fwdata"), "incremental");
        using var database = MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA auto_vacuum;";
        Assert.Equal(2L, command.ExecuteScalar());
    }

    [Fact]
    public void AnOlderBuildIsRefusedWithSomethingTheUserCanActOn()
    {
        var path = Path.Combine(_root, "project.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }

        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema - 1, new Version(1, 0)));

        // Naming the generations alone tells a user nothing they can do; the remedy has to be in the text.
        Assert.Contains("update Motif", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(MotifSchema.CurrentSchema.ToString(), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACurrentSchemaCreatesTheReadOccurrenceTable()
    {
        var path = Path.Combine(_root, "read-occurrences.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "read-occurrences.fwdata"), "read-occurrences");
        using var database = MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('ReadOccurrences');";
        using var reader = command.ExecuteReader();
        var columns = new List<(string Name, string Type, int NotNull, int PrimaryKey)>()
        {
        };
        while (reader.Read())
            columns.Add((reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetInt32(5)));

        Assert.Equal(
            new[]
            {
                ("TextId", "TEXT", 1, 1),
                ("ParagraphId", "TEXT", 1, 2),
                ("SegmentId", "TEXT", 1, 3),
                ("WordIndex", "INTEGER", 1, 4),
                ("FingerprintJson", "TEXT", 1, 0),
            },
            columns);
    }

    [Theory]
    [InlineData(33)]
    [InlineData(34)]
    [InlineData(35)]
    [InlineData(36)]
    [InlineData(47)]
    [InlineData(49)]
    public void AnOlderSchemaIsUpgradedAndItsBackupIsKept(int oldSchema)
    {
        var path = Path.Combine(_root, "stale.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "stale.fwdata"), "stale");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {oldSchema};";
            command.ExecuteNonQuery();
        }
        Assert.False(MotifDatabase.DeleteIfOtherVersion(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        Assert.Equal(MotifSchema.CurrentSchema, PragmaUserVersion(path));
        Assert.Equal(oldSchema, PragmaUserVersion(path + $".schema{oldSchema}.bak"));
    }

    [Fact]
    public void ABaselinesTableWithoutSourceLastWriteUtcIsRefusedRatherThanBackfilled()
    {
        var path = Path.Combine(_root, "no-source-last-write.motif.db");
        var locator = new ProjectLocator(
            Path.Combine(_root, "no-source-last-write.fwdata"), "no-source-last-write");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // A damaged current schema must not be silently repaired.
            command.CommandText = "DROP TABLE Baselines; CREATE TABLE Baselines (" +
                "ProjectKey TEXT PRIMARY KEY, ProjectIdentity TEXT NOT NULL, " +
                "SemanticSnapshotDigest TEXT NOT NULL, ProjectionVersion TEXT NOT NULL, " +
                "CapturedUtc TEXT NOT NULL, BundleDigest TEXT NOT NULL, CapturedHostSessionId TEXT NULL, " +
                "CapturedEditGeneration INTEGER NULL, RootDirectory TEXT NOT NULL, FwDataPath TEXT NOT NULL, " +
                "PublishedUtc TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        var refusal = Assert.Throws<InvalidDataException>(() => MotifDatabase.OpenOwned(
            path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.Contains("Baselines", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAssessedWordsTableWithoutElapsedMsIsRefusedRatherThanBackfilled()
    {
        var path = Path.Combine(_root, "no-elapsed-ms.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "no-elapsed-ms.fwdata"), "no-elapsed-ms");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            // A damaged current schema must not be silently repaired.
            command.CommandText = "DROP TABLE AssessedWords; CREATE TABLE AssessedWords (" +
                "AssessedWordId INTEGER PRIMARY KEY AUTOINCREMENT, " +
                "AssessmentId TEXT NOT NULL REFERENCES Assessments(AssessmentId), " +
                "OrdinalIndex INTEGER NOT NULL, Word TEXT NOT NULL, Outcome TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        var refusal = Assert.Throws<InvalidDataException>(() => MotifDatabase.OpenOwned(
            path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.Contains("AssessedWords", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCurrentSchemaKeepsItsMinimumWorkerVersion()
    {
        var path = Path.Combine(_root, "floor.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "floor.fwdata"), "floor");

        // Schema refusal is separate from this compatible worker-version floor.
        using var database = MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema,
            new Version(1, 0));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT MinimumWorkerVersion FROM MotifMetadata WHERE Id = 1;";
        Assert.Equal("0.1", command.ExecuteScalar() as string);
    }

    private static int PragmaUserVersion(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
