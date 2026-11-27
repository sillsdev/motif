using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using Xunit;
using static SIL.Motif.Tests.Commands.StoreSchemaFixtures;

namespace SIL.Motif.Tests.Commands;

/// <summary>Each supported old schema, as a small store built by <see cref="StoreSchemaFixtures"/>, upgraded through the real open path.</summary>
public sealed class StoreUpgradeFixtureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade-fixture", Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_root, "real", "project.motif.db");
    private string DryCopy => Path.Combine(_root, "dry", "project.motif.db");
    private ProjectLocator Project => new(Path.Combine(_root, "project.fwdata"), "fixture-project");

    // Columns the upgrade rewrites or stamps; every other authored column is carried byte for byte.
    private static readonly HashSet<string> Rewritten =
    [
        "Proposals.AnchorJson", "Proposals.Note", "Proposals.NoteIntentDigest", "Proposals.NoteUpdatedUtc",
        "NamedSelections.PerWordParserWorkLimit", "NamedSelections.PerWordStepLimit", "ProposalDifferences.WordDetailDroppedUtc",
    ];

    public static IEnumerable<object[]> Schemas() => StoreSchemaFixtures.All.Select(schema => new object[] { schema });

    [Fact]
    public void EveryStepHasAFixtureOfTheSchemaBeforeIt()
    {
        // A schema bump adds a step; the store from before it must be buildable, or the bump is untested.
        foreach (var step in StoreUpgradeSteps.All.Where(step => step.To > 49))
            Assert.Contains(step.To - 1, StoreSchemaFixtures.All);
        Assert.Contains(MotifSchema.CurrentSchema - 1, StoreSchemaFixtures.All);
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void TheFixtureIsInTheShapeOfItsSchema(int schema)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Create(Source, Project, schema);
        using var db = Open(Source);
        Assert.Equal(schema, Scalar(db, "PRAGMA user_version;"));
        Assert.Equal(schema >= 55 ? 1 : 0, Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ProposalDifferences';"));
        Assert.Equal(schema >= 56 ? 1 : 0, Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('ProposalDifferences') WHERE name = 'Reference';"));
        Assert.Equal(schema >= 75 ? 1 : 0, Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'ProposalComments';"));
        Assert.Equal(schema >= 77 ? 1 : 0, Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'GrammarCoverageMeasurements';"));
        Assert.Equal(0, Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'WordTypeDispositions';"));
        if (schema is >= 50 and < 60)
            Assert.Equal(1, Scalar(db, "SELECT COUNT(*) FROM pragma_table_info('NamedSelections') WHERE name = 'PerWordStepLimit';"));
    }

    [Theory]
    [MemberData(nameof(Schemas))]
    public void AnOldStoreUpgradesThroughTheRealOpenAndTheDryRunPredictedIt(int schema)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
        Directory.CreateDirectory(Path.GetDirectoryName(DryCopy)!);
        Create(Source, Project, schema);
        File.Copy(Source, DryCopy);
        // The dry run takes a read lease on the store's lock file, which an old store made by hand may not have yet.
        File.WriteAllBytes(DryCopy + ".use.lock", []);
        var original = File.ReadAllBytes(Source);

        var dry = MotifDatabase.DryRunUpgrade(DryCopy, Project, new Version(1, 0))!;
        Assert.Equal(original, File.ReadAllBytes(DryCopy));

        using (MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        SqliteConnection.ClearAllPools();
        var real = JsonSerializer.Deserialize<MotifStoreUpgrade.Report>(File.ReadAllText(Source + ".upgrade.json"))!;

        // The dry run predicts the real result.
        Assert.Equal(schema, real.SourceSchema);
        Assert.Equal(MotifSchema.CurrentSchema, real.TargetSchema);
        Assert.Equal(Predictable(real), Predictable(dry));
        Assert.Equal(StoreUpgradeSteps.PlanFor(schema).Crossed.Select(step => $"{step.To}: {step.Summary}"), real.Steps);

        // The backup exists, verifies and opens.
        Assert.False(File.Exists(Source + $".schema{schema}.bak"));
        Assert.StartsWith(Path.Combine(_root, "real", "project.motif-backups", $"pre-upgrade-schema{schema}-"), real.BackupDirectory, StringComparison.Ordinal);
        var receipt = JsonSerializer.Deserialize<StoreBackupResponse>(File.ReadAllText(real.BackupReceiptPath!), StoreBackups.ReceiptOptions)!;
        Assert.Equal(schema, receipt.SchemaVersion);
        StoreBackups.Verify(receipt, schema);
        using var backup = Open(Path.Combine(real.BackupDirectory!, StoreBackups.StoreName));
        Assert.Equal(schema, Scalar(backup, "PRAGMA user_version;"));
        Assert.Equal(2, Scalar(backup, "SELECT COUNT(*) FROM Proposals;"));

        using var upgraded = Open(Source);
        Assert.Equal(MotifSchema.CurrentSchema, Scalar(upgraded, "PRAGMA user_version;"));
        AssertAuthoredDataIsCarried(schema, real, backup, upgraded);
        AssertDerivedDataIsDroppedAndCounted(schema, real, upgraded);
        AssertStepEffects(schema, upgraded);
        AssertSchemaMatchesAFreshStore(upgraded);

        // A second open has nothing to do and takes no further backup.
        var backups = Directory.GetDirectories(Path.Combine(_root, "real", "project.motif-backups")).Length;
        SqliteConnection.ClearAllPools();
        using (MotifDatabase.OpenOwned(Source, Project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        Assert.Equal(backups, Directory.GetDirectories(Path.Combine(_root, "real", "project.motif-backups")).Length);
    }

    private static void AssertAuthoredDataIsCarried(int schema, MotifStoreUpgrade.Report report, SqliteConnection old, SqliteConnection upgraded)
    {
        Assert.Equal(2, report.Carried["Proposals"]);
        Assert.Equal(2, report.Carried["ProposalRevisions"]);
        foreach (var table in StoreUpgradeSteps.PlanFor(schema).Kept)
        {
            if (table == "ProposalDifferences" || Scalar(old, $"SELECT COUNT(*) FROM sqlite_master WHERE name = '{table}';") == 0) continue;
            var columns = Columns(old, table).Intersect(Columns(upgraded, table)).Where(column => !Rewritten.Contains(table + "." + column)).ToArray();
            Assert.NotEmpty(columns);
            Assert.Equal(Digest(old, table, columns), Digest(upgraded, table, columns));
            Assert.Equal(Scalar(old, $"SELECT COUNT(*) FROM \"{table}\";"), report.Carried[table]);
        }
        // The revision bytes, whatever they hold, are carried as stored.
        Assert.Equal("00FF10EE", Hex(upgraded, "SELECT hex(ProposalJson) FROM ProposalRevisions WHERE ProposalId = 'p-applied';"));
    }

    private static void AssertDerivedDataIsDroppedAndCounted(int schema, MotifStoreUpgrade.Report report, SqliteConnection upgraded)
    {
        Assert.Contains("Jobs", report.Dropped);
        Assert.Equal(1L, report.DroppedCounts!["Jobs"]);
        Assert.Equal(0, Scalar(upgraded, "SELECT COUNT(*) FROM Jobs;"));
        if (schema >= 75)
        {
            Assert.Equal(1L, report.DroppedCounts["ProgressState"]);
            Assert.Equal(0, Scalar(upgraded, "SELECT COUNT(*) FROM ProgressState;"));
        }
        if (schema >= 77)
        {
            Assert.Equal(1L, report.DroppedCounts["GrammarCoverageMeasurements"]);
            Assert.Equal(0, Scalar(upgraded, "SELECT COUNT(*) FROM GrammarCoverageMeasurements;"));
        }
        if (schema == 55)
        {
            // Summaries before 56 cannot be read, so every one is dropped and counted.
            Assert.False(report.Carried.ContainsKey("ProposalDifferences"));
            Assert.Equal(2L, report.DroppedCounts["ProposalDifferences"]);
        }
        if (schema >= 56)
        {
            // Only the decided Proposal's summary is carried; the open one's is dropped and counted.
            Assert.Equal(1, report.Carried["ProposalDifferences"]);
            Assert.Equal(1L, report.DroppedCounts["ProposalDifferences"]);
            Assert.Equal("d-applied", Text(upgraded, "SELECT DifferenceId FROM ProposalDifferences;"));
        }
        // Every dropped table has its rows counted, and none is also carried.
        Assert.All(report.Dropped, table => Assert.False(report.Carried.ContainsKey(table), table));
    }

    // The upgraded store defines the same tables, columns and indexes as a new one.
    private void AssertSchemaMatchesAFreshStore(SqliteConnection upgraded)
    {
        var fresh = Path.Combine(_root, "fresh", "project.motif.db");
        Directory.CreateDirectory(Path.GetDirectoryName(fresh)!);
        using (MotifDatabase.OpenOwned(fresh, Project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
        SqliteConnection.ClearAllPools();
        using var current = Open(fresh);
        Assert.Equal(NormalisedSchema(current), NormalisedSchema(upgraded));
    }

    private static void AssertStepEffects(int schema, SqliteConnection upgraded)
    {
        if (schema != 49)
        {
            // A schema 61 or older Note is folded into a Comment; a newer store's Comment is carried as it was.
            Assert.Equal(1, Scalar(upgraded, "SELECT COUNT(*) FROM ProposalComments;"));
            Assert.Equal(Note, Text(upgraded, "SELECT Text FROM ProposalComments WHERE ProposalId = 'p-open';"));
            Assert.Equal(5000, Scalar(upgraded, "SELECT PerWordParserWorkLimit FROM NamedSelections WHERE SelectionName = 'sel';"));
        }
        Assert.Equal(0, Scalar(upgraded, "SELECT COUNT(*) FROM Proposals WHERE AnchorJson IS NOT NULL;"));
    }

    // Everything a dry run says, apart from the backup it does not take.
    private static string Predictable(MotifStoreUpgrade.Report report) => JsonSerializer.Serialize(new
    {
        report.SourceSchema, report.TargetSchema,
        Carried = report.Carried.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        Dropped = report.Dropped.Order(StringComparer.Ordinal).ToArray(),
        DroppedCounts = report.DroppedCounts!.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray(),
        report.Steps,
        Unreadable = report.UnreadableRevisions.Select(revision => revision.ProposalId + "/" + revision.IntentDigest).ToArray(),
    });

    private static string[] Columns(SqliteConnection db, string table)
    {
        using var command = db.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{table}');";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return [.. names];
    }

    private static long Scalar(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static string Text(SqliteConnection db, string sql) => Hex(db, sql);

    private static string Hex(SqliteConnection db, string sql)
    {
        using var command = db.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
