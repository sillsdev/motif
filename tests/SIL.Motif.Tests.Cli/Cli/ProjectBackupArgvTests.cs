using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>project backup</c> and <c>project restore</c> through the CLI: JSON receipt, round trip and usage lines.</summary>
public sealed class ProjectBackupArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-project-backup-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-project-backup-worker-" + Guid.NewGuid().ToString("N"));

    public ProjectBackupArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(Project, "<languageproject />");
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
    }

    private string Project => Path.Combine(_root, "p.fwdata");

    [Fact]
    public void TheBackupPrintsOneJsonReceiptAndTheRestoreReadsItBack()
    {
        var folder = Path.Combine(_root, "backup");
        var backup = Run($"project backup --project \"{Project}\" --to \"{folder}\" --json");

        Assert.True(backup.ExitCode == 0, backup.Error);
        using (var document = JsonDocument.Parse(backup.Output))
        {
            var root = document.RootElement;
            Assert.Equal(folder, root.GetProperty("backupDirectory").GetString());
            Assert.False(root.GetProperty("fwDataIncluded").GetBoolean());
            var store = root.GetProperty("files")[0];
            Assert.Equal("store", store.GetProperty("role").GetString());
            Assert.Equal(64, store.GetProperty("sha256").GetString()!.Length);
            Assert.True(store.GetProperty("bytes").GetInt64() > 0);
        }

        var restore = Run($"project restore \"{folder}\" --project \"{Project}\" --json");

        Assert.True(restore.ExitCode == 0, restore.Error);
        using var restored = JsonDocument.Parse(restore.Output);
        Assert.Equal(Path.Combine(_root, "p.motif.db"), restored.RootElement.GetProperty("storePath").GetString());
        Assert.Equal(JsonValueKind.Object, restored.RootElement.GetProperty("preRestoreBackup").ValueKind);
    }

    [Fact]
    public async Task ABackupInASecondProcessIsConsistentWhileThisProcessHoldsAReadSnapshotAndKeepsWriting()
    {
        var store = Path.Combine(_root, "p.motif.db");
        using var database = MotifDatabase.OpenOwned(store,
            new SIL.Motif.Contract.Projects.ProjectLocator(Project, "p"), MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var reader = database.OpenConnection();
        using var readTransaction = reader.BeginTransaction(deferred: true);
        using (var count = reader.CreateCommand())
        {
            count.Transaction = readTransaction;
            count.CommandText = "SELECT COUNT(*) FROM StoreRelocations;";
            Assert.Equal(0L, count.ExecuteScalar());
        }
        using var stop = new CancellationTokenSource();
        var written = 0;
        var writer = Task.Run(() =>
        {
            using var connection = database.OpenConnection();
            while (!stop.IsCancellationRequested)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
                    "VALUES ('w', 'x', 't', 'name');";
                insert.ExecuteNonQuery();
                Interlocked.Increment(ref written);
            }
        });
        SpinWait.SpinUntil(() => Volatile.Read(ref written) > 20, TimeSpan.FromSeconds(30));

        var folder = Path.Combine(_root, "live-backup");
        var backup = Run($"project backup --project \"{Project}\" --to \"{folder}\" --json");
        stop.Cancel();
        await writer;

        Assert.True(backup.ExitCode == 0, backup.Error);
        using var copy = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(folder, "store.sqlite"), Mode = SqliteOpenMode.ReadOnly, Pooling = false,
        }.ToString());
        copy.Open();
        using var command = copy.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", command.ExecuteScalar());
        command.CommandText = "SELECT COUNT(*) FROM StoreRelocations;";
        Assert.InRange((long)command.ExecuteScalar()!, 20, Volatile.Read(ref written));
    }

    [Fact]
    public void ARestoreOfAFolderWithNoReceiptRefusesWithItsCode()
    {
        var result = Run($"project restore \"{_root}\" --project \"{Project}\" --json");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Contains(RefusalCodes.ProjectBackupInvalid, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingArgumentsPrintTheUsageLines()
    {
        var backup = Run("project backup");
        Assert.Equal(1, backup.ExitCode);
        Assert.Contains("project backup --project <fwdata> [--to <dir>]", backup.Error, StringComparison.Ordinal);

        var restore = Run($"project restore --project \"{Project}\"");
        Assert.Equal(1, restore.ExitCode);
        Assert.Contains("project restore <backup-dir> --project <fwdata> [--force]", restore.Error, StringComparison.Ordinal);
        Assert.Contains(CommandCatalog.All, command => command.Name == "project backup");
        Assert.Contains(CommandCatalog.All, command => command.Name == "project restore");
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
