using System.Collections.Generic;
using System.Diagnostics;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
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
        Assert.NotEmpty(check.Findings);

        var human = Run("grammar", "check", "--project", project);

        Assert.Equal(0, human.ExitCode);
        Assert.Contains($"Grammar findings: {check.Findings.Count}", human.Output, StringComparison.Ordinal);
        Assert.Empty(human.Error);

        var warnings = Run("warnings", "--project", project, "--json");

        Assert.Equal(0, warnings.ExitCode);
        var response = ProjectionJson.Deserialize<WarningsResponse>(warnings.Output)!;
        Assert.True(response.HasCheck);
        Assert.Equal(check.Findings.Count, response.TotalCount);
    }

    [Fact]
    public void GrammarCheckWithoutBaselineExplainsHowToCaptureOneInTextAndJson()
    {
        Directory.CreateDirectory(_managedRoot);
        var project = pristine.CopyProjectFile();

        var json = Run("grammar", "check", "--project", project, "--json");
        var text = Run("grammar", "check", "--project", project);

        Assert.Equal(0, json.ExitCode);
        Assert.False(ProjectionJson.Deserialize<GrammarCheckResponse>(json.Output)!.HasBaseline);
        Assert.Equal(0, text.ExitCode);
        Assert.Contains("No Baseline has been captured.", text.Output, StringComparison.Ordinal);
        Assert.Contains("motif baseline capture <fwdata>", text.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("grammar", null, null)]
    [InlineData("grammar", "foo", "unused.fwdata")]
    [InlineData("grammar", "check", null)]
    public void InvalidGrammarArgumentsPrintTheUsageLineAndExitOne(
        string command, string? subcommand, string? project)
    {
        var arguments = new List<string> { command };
        if (subcommand is not null) arguments.Add(subcommand);
        if (project is not null)
        {
            arguments.Add("--project");
            arguments.Add(project);
        }

        var result = Run(arguments.ToArray());

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("grammar check --project <fwdata>", result.Output + result.Error, StringComparison.Ordinal);
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
