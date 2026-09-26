using System.Diagnostics;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class GrammarCheckArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot = Path.Combine(
        Path.GetTempPath(), "motif-grammar-check-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void GrammarCheckWritesTheFindingsReadByWarnings()
    {
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var checkedGrammar = Run("grammar", "check", "--project", project, "--json");

        Assert.True(checkedGrammar.ExitCode == 0, checkedGrammar.Error);
        var check = ProjectionJson.Deserialize<GrammarCheckResponse>(checkedGrammar.Output)!;
        Assert.True(check.HasBaseline);

        var warnings = Run("warnings", "--project", project, "--json");

        Assert.Equal(0, warnings.ExitCode);
        var response = ProjectionJson.Deserialize<WarningsResponse>(warnings.Output)!;
        Assert.True(response.HasCheck);
        Assert.Equal(check.Findings.Count, response.TotalCount);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private CliRun Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _managedRoot;
        start.Environment[PanGlossExecutable.PathVariable] = FakeParser.ExecutablePath;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CliRun(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
