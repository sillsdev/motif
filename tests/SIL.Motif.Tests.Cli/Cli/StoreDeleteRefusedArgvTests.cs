using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// <c>store delete-refused</c> reaches the same deletion the window's Delete this file and reopen does, as a
/// developer command: it deletes a store another version of Motif made, and is refused on the released surface.
/// </summary>
public sealed class StoreDeleteRefusedArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-store-delete-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-store-delete-worker-" + Guid.NewGuid().ToString("N"));

    public StoreDeleteRefusedArgvTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(Project, "<languageproject/>");
    }

    private string Project => Path.Combine(_root, "project.fwdata");

    private string StorePath => Path.GetFullPath(Path.Combine(_root, "project.motif.db"));

    [Fact]
    public void ARefusedStoreIsDeletedAndReportedAsJson()
    {
        RefusedStore();

        var result = Run($"store delete-refused --project \"{Project}\" --json", developerCommands: true);

        Assert.Equal(0, result.ExitCode);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("deleted").GetBoolean());
        Assert.Equal(StorePath, document.RootElement.GetProperty("storePath").GetString());
        Assert.False(File.Exists(StorePath));
        Assert.True(File.Exists(Project));
    }

    [Fact]
    public void TheReleasedSurfaceRefusesItAndKeepsTheStore()
    {
        RefusedStore();

        var result = Run($"store delete-refused --project \"{Project}\" --json", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("command.not-in-release", result.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(StorePath));
    }

    [Fact]
    public void AMissingProjectFlagPrintsItsUsage()
    {
        var result = Run("store delete-refused", developerCommands: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("store delete-refused --project <fwdata>", result.Error, StringComparison.Ordinal);
        Assert.Contains(CommandCatalog.All, command => command.Name == "store delete-refused");
    }

    private void RefusedStore()
    {
        Assert.True(ProjectStoreCommand.Run<string>(
            Project, "1.0", (_, _) => CommandOutcome<string>.Success(string.Empty)).Succeeded);
        using var connection = new SqliteConnection($"Data Source={StorePath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {MotifSchema.CurrentSchema - 1};";
        command.ExecuteNonQuery();
    }

    private CliRun Run(string arguments, bool developerCommands)
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
        start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = developerCommands ? "1" : null;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        Assert.True(process.WaitForExit(60000), "The CLI did not exit within its bound.");
        return new CliRun(process.ExitCode, output, error);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
