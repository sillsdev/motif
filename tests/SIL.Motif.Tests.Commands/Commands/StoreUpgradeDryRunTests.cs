using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary><c>store upgrade --dry-run</c> at the command layer: it says what would happen and never upgrades.</summary>
public sealed class StoreUpgradeDryRunTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade-dry-" + Guid.NewGuid().ToString("N"));

    public StoreUpgradeDryRunTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, "<languageproject />");
    }

    private string Project => Path.Combine(_root, "Koro.fwdata");
    private string Store => Path.Combine(_root, "Koro.motif.db");

    [Fact]
    public void AProjectWithNoStoreSaysSoAndCreatesNone()
    {
        var outcome = DryRun();

        Assert.True(outcome.Succeeded);
        Assert.True(JsonDocument.Parse(outcome.Value!).RootElement.GetProperty("storeAbsent").GetBoolean());
        Assert.False(File.Exists(Store));
    }

    [Fact]
    public void ACurrentStoreIsUpToDate()
    {
        CreateStore();

        var outcome = DryRun();

        Assert.True(outcome.Succeeded);
        Assert.True(JsonDocument.Parse(outcome.Value!).RootElement.GetProperty("upToDate").GetBoolean());
    }

    [Fact]
    public void AnOlderStoreReportsTheUpgradeAndIsLeftAtItsSchema()
    {
        CreateStore();
        SetVersion(MotifSchema.CurrentSchema - 1);

        var outcome = DryRun();

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var report = JsonDocument.Parse(outcome.Value!).RootElement;
        Assert.Equal(MotifSchema.CurrentSchema - 1, report.GetProperty("SourceSchema").GetInt32());
        Assert.Equal(MotifSchema.CurrentSchema, report.GetProperty("TargetSchema").GetInt32());
        Assert.Equal(MotifSchema.CurrentSchema - 1L, Version());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*.motif-backups"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*.upgrade.json"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, ".motif-upgrade-dry-run-*"));
    }

    [Fact]
    public void ANewerStoreIsRefusedAsNewerAndNeverOffersAnUpgrade()
    {
        CreateStore();
        SetVersion(MotifSchema.CurrentSchema + 1);

        var outcome = DryRun();

        Assert.False(outcome.Succeeded);
        Assert.Equal("store.other-version", outcome.Refusal!.Code);
        Assert.Equal("true", outcome.Refusal.Facts["storeNewer"]);
        Assert.False(outcome.Refusal.Facts.ContainsKey("storeUpgradable"));
        Assert.Equal(MotifSchema.CurrentSchema + 1L, Version());
    }

    [Fact]
    public void ACurrentStoreThatNeedsANewerWorkerIsRefusedLikeARealOpen()
    {
        CreateStore();
        Execute("UPDATE MotifMetadata SET MinimumWorkerVersion = '999.0.0';");

        var outcome = DryRun();

        Assert.False(outcome.Succeeded);
        Assert.Equal("store.other-version", outcome.Refusal!.Code);
        Assert.Equal("true", outcome.Refusal.Facts["storeNewer"]);
    }

    private CommandOutcome<string> DryRun() => ProjectStoreCommand.DryRunUpgrade(Project, MotifProductVersion.CurrentText);

    private void CreateStore()
    {
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
    }

    private void SetVersion(int schema) => Execute($"PRAGMA user_version = {schema};");

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Store};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long Version()
    {
        using var connection = new SqliteConnection($"Data Source={Store};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
