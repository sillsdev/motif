using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// An older project store is backed up before it is upgraded, with the same backup <c>store backup</c> takes, so
/// <c>store restore</c> puts the original back, and the caller is told what happened.
/// </summary>
public sealed class StoreUpgradeBackupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade-backup-" + Guid.NewGuid().ToString("N"));
    private readonly int _older = MotifSchema.CurrentSchema - 1;

    public StoreUpgradeBackupTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, "<languageproject />");
        Assert.True(Open().Succeeded);
        SqliteConnection.ClearAllPools();
        Execute(Store, $"INSERT INTO Proposals (ProposalId, Status) VALUES ('original', 'applied'); PRAGMA user_version = {_older};");
    }

    private string Project => Path.Combine(_root, "Koro.fwdata");

    private string Store => Path.Combine(_root, "Koro.motif.db");

    [Fact]
    public void BackupThenUpgradeThenRestoreBringsTheOriginalBackAndItUpgradesAgain()
    {
        var explicitBackup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Path.Combine(_root, "mine"))).Value!;
        using (var upgrades = StoreUpgradeNotices.Begin())
        {
            Assert.True(Open().Succeeded);
            var notice = Assert.Single(upgrades.Notices);
            Assert.Equal((_older, MotifSchema.CurrentSchema), (notice.FromSchema, notice.ToSchema));
            Assert.Equal(1L, Scalar(Store, "SELECT COUNT(*) FROM Proposals WHERE ProposalId = 'original';"));
            Assert.Equal((long)MotifSchema.CurrentSchema, Scalar(Store, "PRAGMA user_version;"));

            var restored = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, notice.BackupDirectory!));

            Assert.True(restored.Succeeded, restored.Refusal?.Message);
            Assert.Equal((long)_older, Scalar(Store, "PRAGMA user_version;"));
            Assert.Equal(1L, Scalar(Store, "SELECT COUNT(*) FROM Proposals WHERE ProposalId = 'original';"));
        }
        Assert.Equal(_older, explicitBackup.SchemaVersion);

        using var again = StoreUpgradeNotices.Begin();
        Assert.True(Open().Succeeded);
        Assert.Single(again.Notices);
        Assert.Equal((long)MotifSchema.CurrentSchema, Scalar(Store, "PRAGMA user_version;"));
    }

    [Fact]
    public void TheNoticeNamesTheSchemasTheBackupAndWhatWasDropped()
    {
        Execute(Store, "INSERT INTO Jobs (JobId, ProjectKey, Kind, Status, LineageId, InputJson, CreatedUtc, UpdatedUtc) " +
            "VALUES ('j', 'p', 'k', 's', 'l', '{}', 't', 't');");
        using var upgrades = StoreUpgradeNotices.Begin();

        Assert.True(Open().Succeeded);

        var notice = Assert.Single(upgrades.Notices);
        Assert.StartsWith(
            $"Your Motif project store was upgraded from schema {_older} to {MotifSchema.CurrentSchema}; a backup is at {notice.BackupDirectory}; " +
            "these derived results were dropped: ", notice.Message, StringComparison.Ordinal);
        Assert.Contains("Jobs (1)", notice.Message);
        Assert.Equal(1L, notice.DroppedCounts["Jobs"]);
        Assert.True(File.Exists(notice.ReceiptPath));
    }

    [Fact]
    public void AFailedBackupIsRefusedWithTheCodeTheStepAndNothingChanged()
    {
        File.WriteAllText(Path.Combine(_root, "Koro.motif-backups"), "in the way");
        var before = File.ReadAllBytes(Store);

        var outcome = Open();

        Assert.False(outcome.Succeeded);
        Assert.Equal(RefusalCodes.StoreUpgradeFailed, outcome.Refusal!.Code);
        Assert.Equal("backup", outcome.Refusal.Facts[RefusalFactNames.FailedStep]);
        Assert.False(outcome.Refusal.Facts.ContainsKey(RefusalFactNames.BackupPath));
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.False(File.Exists(Store + $".schema{_older}.bak"));
    }

    [Fact]
    public void AReadOfAnOlderStoreUpgradesItWhenTheRequestMayAndRefusesItWhenItMayNot()
    {
        CommandOutcome<string> Read() => ProjectStoreCommand.ReadExisting(Project, CommandOutcome<string>.Success("absent"),
            (_, _) => CommandOutcome<string>.Success("read"));

        Assert.Equal(RefusalCodes.StoreOtherVersion, Read().Refusal!.Code);
        Assert.Equal((long)_older, Scalar(Store, "PRAGMA user_version;"));

        using (StoreUpgradeContext.UpgradeOnRead())
        using (var upgrades = StoreUpgradeNotices.Begin())
        {
            Assert.Equal("read", Read().Value);
            Assert.Single(upgrades.Notices);
        }
        Assert.Equal((long)MotifSchema.CurrentSchema, Scalar(Store, "PRAGMA user_version;"));
    }

    private CommandOutcome<string> Open() => ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
        (_, _) => CommandOutcome<string>.Success("opened"));

    private static void Execute(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long Scalar(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
