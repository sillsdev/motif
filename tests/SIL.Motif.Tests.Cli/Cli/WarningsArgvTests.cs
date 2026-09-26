using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WarningsArgvTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-warnings-argv-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void KindAndLeftOutFlagsFilterTheJsonAndHumanText()
    {
        Directory.CreateDirectory(_root);
        var project = pristine.CopyProjectFile();
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var response = new GrammarCheckResponse([
            new GrammarWarning(GrammarDiagnosticLevel.Warning, "Unused rule", [], [], "warning: hc-unused-rule: unused")
            { Code = "hc-unused-rule", Group = "Unused rule", Description = "unused" },
            new GrammarWarning(GrammarDiagnosticLevel.Information, "Undeclared segment", [], [],
                "info: hc-undeclared-segment: undeclared")
            { Code = "hc-undeclared-segment", Group = "Undeclared segment", Description = "undeclared" },
        ], HasBaseline: true);
        var saved = ProjectStoreCommand.Run(project, MotifProductVersion.CurrentText, (database, _) =>
        {
            var token = JsonSerializer.Serialize(captured.Value!.Token, MotifJson.CreateOptions());
            new GrammarCheckRepository(database).Save(token, string.Empty, null, response);
            return CommandOutcome<GrammarCheckResponse>.Success(response);
        });
        Assert.True(saved.Succeeded, saved.Refusal?.Message);

        var kind = Run("warnings", "--project", project, "--kind", "hc-undeclared-segment", "--json");
        var leftOut = Run("warnings", "--project", project, "--left-out", "--json");
        var human = Run("warnings", "--project", project, "--left-out");

        Assert.Equal(0, kind.ExitCode);
        Assert.Equal("hc-undeclared-segment", Assert.Single(ProjectionJson.Deserialize<WarningsResponse>(kind.Output)!.Findings).Code);
        Assert.Equal(0, leftOut.ExitCode);
        Assert.Equal("hc-unused-rule", Assert.Single(ProjectionJson.Deserialize<WarningsResponse>(leftOut.Output)!.Findings).Code);
        Assert.Contains("1 warnings, 0 information", human.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("hc-undeclared-segment", human.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void UnavailableGrammarChecksTellTheCallerWhatToRunNext()
    {
        Directory.CreateDirectory(_root);
        var project = pristine.CopyProjectFile();

        var noBaseline = Run("warnings", "--project", project);

        Assert.Equal(0, noBaseline.ExitCode);
        Assert.Contains("No Baseline has been captured.", noBaseline.Output, StringComparison.Ordinal);
        Assert.Contains("motif baseline capture <fwdata>", noBaseline.Output, StringComparison.Ordinal);

        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(project), _root);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);

        var noCheck = Run("warnings", "--project", project);

        Assert.Equal(0, noCheck.ExitCode);
        Assert.Contains("Grammar not checked yet for this Baseline.", noCheck.Output, StringComparison.Ordinal);
        Assert.Contains("motif grammar check --project <fwdata>", noCheck.Output, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private (int ExitCode, string Output, string Error) Run(params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _root;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
