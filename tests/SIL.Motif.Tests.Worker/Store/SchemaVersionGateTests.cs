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
    public void AStoreWithoutTimingIdentityDomainsIsRefusedWithoutRewritingIt()
    {
        var path = Path.Combine(_root, "untyped-timing.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "untyped-timing.fwdata"), "untyped-timing");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 33;";
            command.ExecuteNonQuery();
        }
        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.Contains("delete", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(33, PragmaUserVersion(path));
    }

    [Fact]
    public void StoresWithLabelBasedNaturalClassReachAreRefusedWithoutRewrite()
    {
        Assert.Equal(37, MotifSchema.CurrentSchema);
        var path = Path.Combine(_root, "natural-class-reach.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 34;";
            command.ExecuteNonQuery();
        }
        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));
        Assert.Contains("delete", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(34, PragmaUserVersion(path));
    }

    [Fact]
    public void StoresWithoutAnalysisComparisonAreRefusedWithoutRewrite()
    {
        var path = Path.Combine(_root, "no-analysis-comparison.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 35;";
            command.ExecuteNonQuery();
        }

        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));

        Assert.Contains("delete", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(35, PragmaUserVersion(path));
    }

    [Fact]
    public void StoresWithoutRecordedPanGlossEvidenceAreRefusedWithoutRewrite()
    {
        var path = Path.Combine(_root, "no-recorded-pangloss-evidence.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 36;";
            command.ExecuteNonQuery();
        }

        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));

        Assert.Contains("delete", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(36, PragmaUserVersion(path));
    }

    [Fact]
    public void AnOlderBuildIsRefusedWithSomethingTheUserCanActOn()
    {
        Assert.Equal(37, MotifSchema.CurrentSchema);
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

    [Fact]
    public void ADatabaseStampedWithADifferentSchemaIsRefusedRatherThanMigrated()
    {
        var path = Path.Combine(_root, "stale.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "stale.fwdata"), "stale");
        using (MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
            command.ExecuteNonQuery();
        }

        var refusal = Assert.Throws<MotifStoreVersionException>(() =>
            MotifDatabase.OpenOwned(path, locator, MotifSchema.CurrentSchema, new Version(1, 0)));

        // Pre-1.0 Motif has no upgrade path, so the remedy is deletion, not a version number to chase.
        Assert.Contains("delete", refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(path, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(MotifSchema.CurrentSchema - 1, PragmaUserVersion(path));
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
            // The shape this task's column replaces: no migration exists, so this must be refused outright.
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
            // The shape this task's column replaces: no migration exists, so this must be refused outright.
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
    public void AddingTheLeaseGenerationDidNotRaiseTheCompatibilityFloor()
    {
        var path = Path.Combine(_root, "floor.motif.db");
        var locator = new ProjectLocator(Path.Combine(_root, "floor.fwdata"), "floor");

        // The columns are additive and nullable, so a build that predates them stays welcome.
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
