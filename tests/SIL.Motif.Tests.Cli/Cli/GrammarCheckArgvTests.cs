using System.Collections.Generic;
using System.Diagnostics;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Preferences;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
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

        Assert.Equal(2, json.ExitCode);
        Assert.Empty(json.Output);
        var refusal = ProjectionJson.Deserialize<FailureEnvelope>(json.Error)!;
        Assert.Equal("baseline.missing", refusal.Code);
        Assert.Contains("motif baseline capture --project <fwdata>", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(2, text.ExitCode);
        Assert.Empty(text.Output);
        Assert.Contains("A Baseline is required", text.Error, StringComparison.Ordinal);
        Assert.Contains("motif baseline capture --project <fwdata>", text.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null, "bounded")]
    [InlineData("all", null, "all")]
    [InlineData("off", null, "off")]
    [InlineData(null, "all", "all")]
    [InlineData(null, "off", "off")]
    [InlineData("budget", "off", "bounded")]
    [InlineData("off", "all", "off")]
    [InlineData("all", "budget", "all")]
    public void TheFlagOverridesTheSavedChoiceAndTheSavedChoiceOverridesTheDefault(
        string? flag, string? saved, string expectedForPanGloss)
    {
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _managedRoot);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_managedRoot, "recording-parser"));
        var preference = Path.Combine(_managedRoot, "preferences", "default-proposal-checks.json");
        if (saved is not null) new FileDefaultProposalChecksStore(preference).SetStoredAnalyses(saved switch
        {
            "all" => StoredAnalysisComparison.All,
            "off" => StoredAnalysisComparison.Off,
            _ => StoredAnalysisComparison.Budget,
        });
        var arguments = new List<string> { "grammar", "check", "--project", project, "--json" };
        if (flag is not null) arguments.AddRange(["--stored-analyses", flag]);

        var result = RunWith(parser, preference, arguments.ToArray());

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal([expectedForPanGloss], FakeParser.StoredAnalysesRequested(parser));
    }

    [Theory]
    [InlineData("bounded")]
    [InlineData("everything")]
    [InlineData("")]
    public void AnUnknownStoredAnalysesWordPrintsTheUsageLineAndNeverStartsPanGloss(string word)
    {
        var project = pristine.CopyProjectFile();
        var parser = FakeParser.CopyRecordingInvocations(Path.Combine(_managedRoot, "recording-parser"));

        var result = RunWith(parser, null, "grammar", "check", "--project", project, "--stored-analyses", word);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("[--stored-analyses budget|all|off]", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Empty(FakeParser.Invocations(parser));
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

    private CliRun Run(params string[] arguments) => RunWith(FakeParser.ExecutablePath, null, arguments);

    private CliRun RunWith(string parserPath, string? preferencePath, params string[] arguments)
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
        start.Environment[PanGlossExecutable.PathVariable] = parserPath;
        if (preferencePath is not null)
            start.Environment[FileDefaultProposalChecksStore.PathEnvironmentVariable] = preferencePath;
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new CliRun(process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
