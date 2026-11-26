using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>store upgrade --project --dry-run</c> through the CLI: it reports and never upgrades or backs up.</summary>
public sealed class StoreUpgradeDryRunArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-upgrade-dry-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-upgrade-dry-cli-worker-" + Guid.NewGuid().ToString("N"));

    public StoreUpgradeDryRunArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(Project, "<languageproject />");
    }

    private string Project => Path.Combine(_root, "p.fwdata");
    private string Store => Path.Combine(_root, "p.motif.db");

    [Fact]
    public void ADryRunOfAnOlderStorePrintsTheReportAndLeavesTheStoreAlone()
    {
        CreateStore();
        SetVersion(MotifSchema.CurrentSchema - 1);

        var run = Run($"store upgrade --project \"{Project}\" --dry-run --json");

        Assert.True(run.ExitCode == 0, run.Error);
        using var report = JsonDocument.Parse(run.Output);
        Assert.Equal(MotifSchema.CurrentSchema - 1, report.RootElement.GetProperty("SourceSchema").GetInt32());
        Assert.Equal(MotifSchema.CurrentSchema - 1L, Version());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*.motif-backups"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*.upgrade.json"));
        Assert.DoesNotContain("Upgraded store schema", run.Error);
    }

    [Fact]
    public void ADryRunOfAProjectWithNoStoreSaysSo()
    {
        var run = Run($"store upgrade --project \"{Project}\" --dry-run");

        Assert.True(run.ExitCode == 0, run.Error);
        Assert.True(JsonDocument.Parse(run.Output).RootElement.GetProperty("storeAbsent").GetBoolean());
        Assert.False(File.Exists(Store));
    }

    [Fact]
    public void ADryRunOfANewerStoreIsRefusedAsOtherVersion()
    {
        CreateStore();
        SetVersion(MotifSchema.CurrentSchema + 1);

        var run = Run($"store upgrade --project \"{Project}\" --dry-run --json");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("store.other-version", run.Output + run.Error);
        Assert.Equal(MotifSchema.CurrentSchema + 1L, Version());
    }

    [Fact]
    public void ADryRunWithAnyOtherFlagIsUsageAndNeverTheRealUpgrade()
    {
        CreateStore();
        SetVersion(MotifSchema.CurrentSchema - 1);

        var run = Run($"store upgrade --project \"{Project}\" --dry-run --source x.db --json");

        Assert.NotEqual(0, run.ExitCode);
        Assert.Contains("InvalidArgument", run.Output + run.Error);
        Assert.Equal(MotifSchema.CurrentSchema - 1L, Version());
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root, "*.motif-backups"));
    }

    private void CreateStore()
    {
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
    }

    private void SetVersion(int schema)
    {
        using var connection = new SqliteConnection($"Data Source={Store};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {schema};";
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

    private CliRun Run(string arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = null;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit((int)Wait.Scaled(TimeSpan.FromMinutes(30)).TotalMilliseconds),
            "The CLI process did not exit within 30 minutes (scaled by MOTIF_TEST_DEADLINE_SCALE).");
        return new CliRun(process.ExitCode, output, error);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
