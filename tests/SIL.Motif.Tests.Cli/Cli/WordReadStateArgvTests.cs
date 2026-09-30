using System.Diagnostics;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WordReadStateArgvTests : IDisposable
{
    private readonly string _workerRoot =
        Path.Combine(Path.GetTempPath(), "motif-word-read-state-" + Guid.NewGuid().ToString("N"));
    private string Project => Path.Combine(_workerRoot, "project.fwdata");

    public WordReadStateArgvTests()
    {
        Directory.CreateDirectory(_workerRoot);
        File.WriteAllText(Project, "<languageproject/>");
    }

    [Fact]
    public void WordReadStateShowsItsCataloguedUsageOnTheDeveloperSurface()
    {
        var result = Run("word read-state", developerCommands: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("word read-state --project <fwdata> --text <textId>", result.Error, StringComparison.Ordinal);
        Assert.Contains(CommandCatalog.All, command => command.Name == "word read-state");
    }

    [Fact]
    public void TheReleasedSurfaceRefusesWordReadState()
    {
        var result = Run("word read-state --json", developerCommands: false);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.Refused), result.ExitCode);
        Assert.Contains("command.not-in-release", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadStateRefusesToMarkReadWithoutACurrentBaseline()
    {
        var textId = Guid.NewGuid();
        var result = Run($"word read-state --project \"{Project}\" --text {textId:D} --read --json",
            developerCommands: true);

        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), result.ExitCode);
        Assert.Contains("word.read-state-invalid", result.Error, StringComparison.Ordinal);
        Assert.Contains("A current Baseline is required", result.Error, StringComparison.Ordinal);
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
        try { Directory.Delete(_workerRoot, recursive: true); } catch (IOException) { }
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
