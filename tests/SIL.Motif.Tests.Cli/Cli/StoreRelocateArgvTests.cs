using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary><c>store relocate</c> through the CLI: confirmation by <c>--confirm</c>, JSON result, and its usage line.</summary>
public sealed class StoreRelocateArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-store-relocate-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-store-relocate-worker-" + Guid.NewGuid().ToString("N"));

    public StoreRelocateArgvTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "a"));
        Directory.CreateDirectory(Path.Combine(_root, "b"));
        Directory.CreateDirectory(_workerRoot);
        var text = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("languageproject",
            new System.Xml.Linq.XElement("rt", new System.Xml.Linq.XAttribute("class", "LangProject"),
                new System.Xml.Linq.XAttribute("guid", Guid.Parse("0d1f9e4a-5b3c-4a8e-9f10-2c3d4e5f6a7b"))))).ToString();
        File.WriteAllText(Original, text);
        File.WriteAllText(Copy, text);
        Assert.True(ProjectStoreCommand.Run(Original, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
        File.Copy(Path.Combine(_root, "a", "p.motif.db"), Path.Combine(_root, "b", "p.motif.db"));
    }

    private string Original => Path.Combine(_root, "a", "p.fwdata");

    private string Copy => Path.Combine(_root, "b", "p.fwdata");

    [Fact]
    public void TheConfirmedCommandRelocatesTheStoreAndReportsJson()
    {
        var result = Run($"store relocate --project \"{Copy}\" --confirm \"{StoreRelocateCommand.ConfirmationPhrase}\" --json");

        Assert.True(result.ExitCode == 0, result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.GetProperty("changed").GetBoolean());
        Assert.Equal(Path.GetFullPath(Copy), document.RootElement.GetProperty("newProjectPath").GetString());
        Assert.Equal(Path.GetFullPath(Original), document.RootElement.GetProperty("oldProjectPath").GetString());
        Assert.True(document.RootElement.GetProperty("oldProjectStillExists").GetBoolean());
    }

    [Fact]
    public void WithoutConfirmationTheCommandRefusesAndNamesThePhrase()
    {
        var result = Run($"store relocate --project \"{Copy}\"");

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Contains(StoreRelocateCommand.ConfirmationPhrase, result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingProjectFlagPrintsItsUsage()
    {
        var result = Run("store relocate");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("store relocate --project <fwdata>", result.Error, StringComparison.Ordinal);
        Assert.Contains(CommandCatalog.All, command => command.Name == "store relocate");
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
