using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>project backup</c> copies the store consistently while it is in use, and <c>project restore</c> puts it back
/// only when the store is free, the backup is intact and no newer than this build, saving what it replaces.
/// </summary>
public sealed class ProjectBackupCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-backup-" + Guid.NewGuid().ToString("N"));

    public ProjectBackupCommandTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, "<languageproject />");
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
    }

    private string Project => Path.Combine(_root, "Koro.fwdata");

    private string Store => Path.Combine(_root, "Koro.motif.db");

    private string Folder(string name) => Path.Combine(_root, name);

    [Fact]
    public void ABackupWritesTheStoreAndAReceiptWithSizesShasAndTheSchema()
    {
        AddMarker(Store, "first");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = []");

        var result = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var receipt = result.Value!;
        Assert.Equal(MotifSchema.CurrentSchema, receipt.SchemaVersion);
        Assert.False(receipt.FwDataIncluded);
        Assert.Contains("not part of this backup", receipt.FwDataNote, StringComparison.Ordinal);
        Assert.Equal(["store", "config"], receipt.Files.Select(file => file.Role));
        foreach (var file in receipt.Files)
        {
            Assert.Equal(new FileInfo(file.Path).Length, file.Bytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))), file.Sha256);
        }
        Assert.Equal(["first"], Markers(Path.Combine(Folder("b1"), "store.sqlite")));
        Assert.Equal(MotifSchema.CurrentSchema, ReadSchema(Path.Combine(Folder("b1"), "store.sqlite")));
        Assert.Empty(Directory.GetFiles(Folder("b1"), "*-wal"));
        using var document = JsonDocument.Parse(File.ReadAllText(receipt.ReceiptPath));
        Assert.False(document.RootElement.GetProperty("fwDataIncluded").GetBoolean());
    }

    [Fact]
    public void WithoutToTheBackupGoesIntoATimestampedFolderBesideTheStore()
    {
        var at = new DateTimeOffset(2026, 11, 25, 8, 30, 15, 123, TimeSpan.Zero);

        var result = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project), new FixedClock(at));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(Path.Combine(_root, "Koro.motif-backup-20261125T083015123Z"), result.Value!.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.Value.BackupDirectory, ProjectBackupCommand.ReceiptName)));
    }

    [Fact]
    public void ABackupTakenWhileAnotherConnectionReadsAndWritesIsOneConsistentSnapshot()
    {
        AddMarker(Store, "before");
        using var database = MotifDatabase.OpenOwned(Store,
            new SIL.Motif.Contract.Projects.ProjectLocator(Project, "Koro"), MotifSchema.CurrentSchema,
            MotifProductVersion.Current);
        using var reader = database.OpenConnection();
        using var readTransaction = reader.BeginTransaction(deferred: true);
        using (var command = reader.CreateCommand())
        {
            command.Transaction = readTransaction;
            command.CommandText = "SELECT COUNT(*) FROM StoreRelocations;";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        using var writer = database.OpenConnection();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
                "VALUES ('during', 'x', 't', 'name');";
            command.ExecuteNonQuery();
        }

        var result = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("live")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var copy = Path.Combine(Folder("live"), "store.sqlite");
        Assert.Equal(["before", "during"], Markers(copy));
        Assert.Equal(["ok"], Scalars(copy, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void ARestoreReturnsTheStoreToTheBackupAndSavesTheStoreItReplaced()
    {
        AddMarker(Store, "kept");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = [\"A\"]");
        var backup = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1"))).Value!;
        AddMarker(Store, "added-later");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = [\"B\"]");

        var result = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(["kept"], Markers(Store));
        Assert.Equal("collect = [\"A\"]", File.ReadAllText(Path.Combine(_root, "Koro.motif.toml")));
        Assert.Equal(MotifSchema.CurrentSchema, result.Value!.SchemaVersion);
        var saved = result.Value.PreRestoreBackup!;
        Assert.StartsWith(Path.Combine(_root, "Koro.motif-backups", "pre-restore-"), saved.BackupDirectory, StringComparison.Ordinal);
        Assert.Equal(["kept", "added-later"], Markers(Path.Combine(saved.BackupDirectory, "store.sqlite")));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        var reopened = ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("opened"));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
    }

    [Fact]
    public void ARestoreWhileTheStoreIsOpenIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1"))).Value!;
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);
        using var lease = new FileStream(Store + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);

        var result = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ProjectBusy, result.Refusal!.Code);
        lease.Dispose();
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void ABackupWhoseSchemaIsNewerThanThisBuildIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1"))).Value!;
        SetSchema(Path.Combine(backup.BackupDirectory, "store.sqlite"), MotifSchema.CurrentSchema + 1);
        RewriteReceipt(backup, schema: MotifSchema.CurrentSchema);
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);

        var byFile = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.ProjectBackupNewerSchema, byFile.Refusal?.Code);
        RewriteReceipt(backup, schema: MotifSchema.CurrentSchema + 1);
        var byReceipt = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.ProjectBackupNewerSchema, byReceipt.Refusal?.Code);
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void AFileThatDoesNotMatchItsShaIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1"))).Value!;
        var copy = Path.Combine(backup.BackupDirectory, "store.sqlite");
        var bytes = File.ReadAllBytes(copy);
        bytes[^1] ^= 0xff;
        File.WriteAllBytes(copy, bytes);
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);

        var result = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.ProjectBackupChecksumMismatch, result.Refusal?.Code);
        Assert.Equal("store.sqlite", result.Refusal!.Facts["file"]);
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        Assert.False(Directory.Exists(Path.Combine(_root, "Koro.motif-backups")));
    }

    [Fact]
    public void AFolderWithoutAReceiptAndABackupOfAnotherProjectAreRefused()
    {
        Directory.CreateDirectory(Folder("empty"));
        var none = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, Folder("empty")));
        Assert.Equal(RefusalCodes.ProjectBackupInvalid, none.Refusal?.Code);

        var backup = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1"))).Value!;
        RewriteReceipt(backup, schema: backup.SchemaVersion, projectPath: Path.Combine(_root, "Other.fwdata"));
        var other = ProjectBackupCommand.Restore(new ProjectRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.ProjectBackupOtherProject, other.Refusal?.Code);
    }

    [Fact]
    public void ABackupOfAProjectWithNoStoreOrToAFullFolderIsRefused()
    {
        Directory.CreateDirectory(Folder("full"));
        File.WriteAllText(Path.Combine(Folder("full"), "x.txt"), "x");
        var full = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("full")));
        Assert.Equal(RefusalCodes.ProjectBackupDestinationExists, full.Refusal?.Code);

        var lonely = Path.Combine(_root, "Lonely.fwdata");
        File.WriteAllText(lonely, "<languageproject />");
        var none = ProjectBackupCommand.Backup(new ProjectBackupRequest(lonely));
        Assert.Equal(RefusalCodes.ProjectBackupNoStore, none.Refusal?.Code);
    }

    [Fact]
    public void ABackupWhileTheStoreIsHeldExclusivelyIsRefusedAsBusy()
    {
        using var exclusive = new FileStream(Store + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
            FileShare.None, 1, FileOptions.None);

        var result = ProjectBackupCommand.Backup(new ProjectBackupRequest(Project, Folder("b1")));

        Assert.Equal(RefusalCodes.ProjectBusy, result.Refusal?.Code);
        Assert.False(Directory.Exists(Folder("b1")));
    }

    private static void RewriteReceipt(ProjectBackupResponse backup, int schema, string? projectPath = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var files = backup.Files.Select(file => file with
        {
            Bytes = new FileInfo(file.Path).Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))),
        }).ToList();
        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(
            backup with { SchemaVersion = schema, ProjectPath = projectPath ?? backup.ProjectPath, Files = files }, options));
    }

    private static void AddMarker(string store, string marker)
    {
        using var connection = Open(store, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
            "VALUES ($marker, 'x', 't', 'name');";
        command.Parameters.AddWithValue("$marker", marker);
        command.ExecuteNonQuery();
    }

    private static void SetSchema(string store, int schema)
    {
        using var connection = Open(store, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {schema};";
        command.ExecuteNonQuery();
    }

    private static int ReadSchema(string store) => int.Parse(Scalars(store, "PRAGMA user_version;").Single());

    private static List<string> Markers(string store) =>
        Scalars(store, "SELECT OldFwDataPath FROM StoreRelocations ORDER BY RelocationId;");

    private static List<string> Scalars(string store, string sql)
    {
        using var connection = Open(store, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        return values;
    }

    private static SqliteConnection Open(string store, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store, Mode = mode, Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
