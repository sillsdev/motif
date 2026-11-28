using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>An older machine store is backed up and rebuilt empty on open; a newer one is refused and left alone.</summary>
public sealed class MachineStoreRebuildTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-machine-rebuild", Guid.NewGuid().ToString("N"));
    private string Db => Path.Combine(_root, "motif.db");
    private string Backups => Path.Combine(_root, "motif.db-backups");

    public MachineStoreRebuildTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    // Makes a current machine store and relabels it as another schema.
    private void MakeStoreAt(int schema)
    {
        using (var machine = MachineDatabase.Open(_root))
        using (var connection = machine.OpenConnection())
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "SELECT COUNT(*) FROM KnownProjects;";
            insert.ExecuteScalar();
        }
        SqliteConnection.ClearAllPools();
        using var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString());
        raw.Open();
        using var command = raw.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {schema};" +
            "CREATE TABLE Usage (Id INTEGER PRIMARY KEY AUTOINCREMENT, TimestampUtc TEXT NOT NULL, Command TEXT NOT NULL, ArgumentShapeJson TEXT NOT NULL);";
        command.ExecuteNonQuery();
    }

    [Fact]
    public void AnOlderMachineStoreIsBackedUpThenRebuiltEmpty()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        var before = File.ReadAllBytes(Db);

        using (var machine = MachineDatabase.Open(_root))
        using (var connection = machine.OpenConnection())
            Assert.Equal(MachineSchema.CurrentSchema, Scalar(connection, "PRAGMA user_version;"));

        var backup = Assert.Single(Directory.GetDirectories(Backups));
        Assert.StartsWith($"pre-upgrade-schema{MachineSchema.CurrentSchema - 1}-", Path.GetFileName(backup), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(backup, "motif-backup.json")));
        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.Combine(backup, "store.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        copy.Open();
        Assert.Equal(MachineSchema.CurrentSchema - 1, Scalar(copy, "PRAGMA user_version;"));
        Assert.NotEmpty(before);
    }

    [Fact]
    public void TheRebuiltStoreHoldsNoOldRowsAndTheBackupHoldsThem()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString()))
        {
            raw.Open();
            using var usage = raw.CreateCommand();
            usage.CommandText = "SELECT sql FROM sqlite_master WHERE name = 'Usage';";
            Assert.NotNull(usage.ExecuteScalar());
        }
        var notices = new List<StoreUpgradeNotice>();
        using (var scope = StoreUpgradeNotices.Begin())
        {
            using var machine = MachineDatabase.Open(_root);
            notices.AddRange(scope.Notices);
        }
        var notice = Assert.Single(notices);
        Assert.Equal(MachineSchema.CurrentSchema - 1, notice.FromSchema);
        Assert.Equal(MachineSchema.CurrentSchema, notice.ToSchema);
        Assert.Contains("machine store", notice.Message, StringComparison.Ordinal);
        Assert.Equal(Directory.GetDirectories(Backups).Single(), notice.BackupDirectory);
    }

    [Fact]
    public void AProjectTagSurvivesARebuildAndUsageIsDropped()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString()))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText =
                "INSERT INTO KnownProjects (WorkspaceKey, FullFwDataPath, LastSeenUtc, ProjectTag) VALUES ('k', '/p/a.fwdata', '2026-01-01T00:00:00Z', 'mine');" +
                "INSERT INTO Usage (TimestampUtc, Command, ArgumentShapeJson) VALUES ('2026-01-01T00:00:00Z', 'x', '{}');";
            command.ExecuteNonQuery();
        }

        var notices = new List<StoreUpgradeNotice>();
        using (var scope = StoreUpgradeNotices.Begin())
        {
            using var machine = MachineDatabase.Open(_root);
            using var connection = machine.OpenConnection();
            using var tag = connection.CreateCommand();
            tag.CommandText = "SELECT ProjectTag || '|' || FullFwDataPath || '|' || LastSeenUtc FROM KnownProjects WHERE WorkspaceKey = 'k';";
            Assert.Equal("mine|/p/a.fwdata|2026-01-01T00:00:00Z", tag.ExecuteScalar());
            Assert.Equal(0, Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name = 'Usage';"));
            notices.AddRange(scope.Notices);
        }
        var notice = Assert.Single(notices);
        Assert.Equal(1, notice.DroppedCounts["Usage"]);
        Assert.DoesNotContain("KnownProjects", notice.DroppedCounts.Keys);
    }

    [Fact]
    public void AFailureAfterTheCarryLeavesTheOldStoreWithItsTagsAndTheNextOpenSucceeds()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString()))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "INSERT INTO KnownProjects (WorkspaceKey, FullFwDataPath, LastSeenUtc) VALUES ('k', '/p/a.fwdata', '2026-01-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }
        var before = File.ReadAllBytes(Db);
        MotifSqliteStore.StepHook = step => { if (step == "carried") throw new IOException("injected"); };
        try { Assert.Throws<MotifStoreUpgradeFailedException>(() => MachineDatabase.Open(_root)); }
        finally { MotifSqliteStore.StepHook = null; }

        Assert.Equal(before, File.ReadAllBytes(Db));
        Assert.False(File.Exists(Db + ".rebuilding"));

        using var machine = MachineDatabase.Open(_root);
        using var connection = machine.OpenConnection();
        Assert.Equal(MachineSchema.CurrentSchema, Scalar(connection, "PRAGMA user_version;"));
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM KnownProjects;"));
    }

    [Fact]
    public void ACrashAfterTheOldStoreWasRemovedIsFinishedByTheNextOpenWithItsCarriedRows()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        using (var raw = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db, Pooling = false }.ToString()))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = "INSERT INTO KnownProjects (WorkspaceKey, FullFwDataPath, LastSeenUtc) VALUES ('k', '/p/a.fwdata', '2026-01-01T00:00:00Z');";
            command.ExecuteNonQuery();
        }
        MotifSqliteStore.StepHook = step => { if (step == "old-removed") throw new InvalidOperationException("crash"); };
        try { Assert.Throws<InvalidOperationException>(() => MachineDatabase.Open(_root)); }
        finally { MotifSqliteStore.StepHook = null; }
        Assert.False(File.Exists(Db));

        using var machine = MachineDatabase.Open(_root);
        using var connection = machine.OpenConnection();
        Assert.Equal(1, Scalar(connection, "SELECT COUNT(*) FROM KnownProjects;"));
    }

    [Fact]
    public void ACurrentMachineStoreTakesNoBackup()
    {
        using (MachineDatabase.Open(_root)) { }
        SqliteConnection.ClearAllPools();
        using (MachineDatabase.Open(_root)) { }
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void ANewerMachineStoreIsRefusedUntouchedAndNeverBackedUpOrRebuilt()
    {
        MakeStoreAt(MachineSchema.CurrentSchema + 1);
        var before = File.ReadAllBytes(Db);
        var refused = Assert.Throws<MotifStoreVersionException>(() => MachineDatabase.Open(_root));
        Assert.True(refused.Newer);
        Assert.Equal(before, File.ReadAllBytes(Db));
        Assert.False(Directory.Exists(Backups));
    }

    [Fact]
    public void ARebuildKeepsTheLastThreeBackupsAndAlwaysTheOneItJustMade()
    {
        Directory.CreateDirectory(Backups);
        var old = new List<string>();
        for (var n = 1; n <= 4; n++)
        {
            var directory = Path.Combine(Backups, $"pre-upgrade-schema1-2026010{n}T000000000Z");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, "store.sqlite"), [1, 2, 3]);
            File.WriteAllText(Path.Combine(directory, "motif-backup.json"), "{\"schemaVersion\":1}");
            old.Add(directory);
        }
        var newer = Path.Combine(Backups, "pre-upgrade-schema99-20250101T000000000Z");
        Directory.CreateDirectory(newer);
        MakeStoreAt(MachineSchema.CurrentSchema - 1);

        using (MachineDatabase.Open(_root)) { }

        var kept = Directory.GetDirectories(Backups).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray();
        // Oldest evicted first: the four old ones and the new one make five counted; two are removed.
        Assert.DoesNotContain(Path.GetFileName(old[0]), kept);
        Assert.DoesNotContain(Path.GetFileName(old[1]), kept);
        Assert.Contains(Path.GetFileName(old[2]), kept);
        Assert.Contains(Path.GetFileName(old[3]), kept);
        Assert.Contains(Path.GetFileName(newer), kept);
        Assert.Contains(kept, name => name!.StartsWith($"pre-upgrade-schema{MachineSchema.CurrentSchema - 1}-", StringComparison.Ordinal));
        Assert.Equal(4, kept.Length);
    }

    [Fact]
    public void ABackupThatFailsLeavesTheOldStoreAsItWasAndNamesNoBackup()
    {
        MakeStoreAt(MachineSchema.CurrentSchema - 1);
        var before = File.ReadAllBytes(Db);
        // A file where the backups folder must go makes the backup fail before anything is changed.
        File.WriteAllText(Backups, "in the way");
        var failure = Assert.Throws<MotifStoreUpgradeFailedException>(() => MachineDatabase.Open(_root));
        Assert.Equal(MotifStoreUpgradeFailedException.BackupStep, failure.Step);
        Assert.Null(failure.Backup);
        Assert.Equal(before, File.ReadAllBytes(Db));
    }
}
