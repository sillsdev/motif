using System.Diagnostics;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AgentHandoffArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-agent-handoff-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public AgentHandoffArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task AHandoffFromARetainedRunWritesFiveFilesAndRefusesAFullFolder()
    {
        var project = _pristine.CopyProjectFile();
        var baseline = await RunAsync(null, "baseline", "capture", project, "--json");
        Assert.Equal(0, baseline.ExitCode);
        var selection = await RunAsync(null, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa", "--json");
        Assert.Equal(0, selection.ExitCode);

        var parser = CopyFakeParser();
        var assess = await RunAsync(parser, "assess", project, "--json");
        Assert.Equal(0, assess.ExitCode);
        var assessed = ProjectionJson.Deserialize<AssessCommandResponse>(assess.Output)!;
        Assert.NotEmpty(assessed.InvocationId);

        var destination = Path.Combine(_root, "handoff");
        var handoffResult = await RunAsync(null, "handoff", project, "--out", destination,
            "--invocation", assessed.InvocationId, "--json");

        Assert.Equal(0, handoffResult.ExitCode);
        var handoff = ProjectionJson.Deserialize<HandoffCommandResponse>(handoffResult.Output)!;
        Assert.Equal(Path.GetFullPath(destination), Path.GetFullPath(handoff.OutputDirectory));
        Assert.Equal(assessed.InvocationId, handoff.InvocationId);
        Assert.Equal(assessed.AssessmentIds.Order(StringComparer.Ordinal), handoff.AssessmentIds.Order(StringComparer.Ordinal));
        Assert.Equal(5, handoff.Files.Count);
        Assert.Equal(new[]
        {
            "assessment.json",
            "grammar.json",
            "handoff.md",
            "parse_grammar_texts_assessment.py",
            "texts.json",
        }, handoff.Files.Order(StringComparer.Ordinal));
        Assert.All(handoff.Files, file => Assert.True(File.Exists(Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar))), file));

        var fullDestination = Path.Combine(_root, "full-folder");
        Directory.CreateDirectory(fullDestination);
        var keep = Path.Combine(fullDestination, "keep.txt");
        File.WriteAllText(keep, "preserve");
        var full = await RunAsync(null, "handoff", project, "--out", fullDestination,
            "--invocation", assessed.InvocationId, "--json");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), full.ExitCode);
        Assert.Equal("handoff.destination-exists", ProjectionJson.Deserialize<FailureEnvelope>(full.Error)!.Code);
        Assert.Equal("preserve", File.ReadAllText(keep));
        Assert.Single(Directory.GetFiles(fullDestination));

        var missingDestination = Path.Combine(_root, "missing-run");
        var missing = await RunAsync(null, "handoff", project, "--out", missingDestination,
            "--invocation", "missing-invocation", "--json");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.NotFound), missing.ExitCode);
        Assert.Equal("handoff.invocation-not-found", ProjectionJson.Deserialize<FailureEnvelope>(missing.Error)!.Code);
        Assert.False(Directory.Exists(missingDestination));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string CopyFakeParser()
    {
        var directory = Path.Combine(_root, "fake-pangloss-" + Guid.NewGuid().ToString("N"));
        var parser = FakeParser.Copy(directory);
        FakeParser.BehaveBesideExecutable(parser, new { words = new[] { new { word = "motifa", outcome = "complete" } } });
        return parser;
    }

    private async Task<CliRun> RunAsync(string? parserPath, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = _workerRoot;
        start.Environment[PanGlossExecutable.PathVariable] = parserPath ?? FakeParser.ExecutablePath;
        start.Environment.Remove("MOTIF_DEVELOPER_COMMANDS");
        start.Environment.Remove("FAKE_PANGLOSS_BEHAVIOUR_PATH");
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return new CliRun(process.ExitCode, await outputTask, await errorTask);
    }

    private sealed record CliRun(int ExitCode, string Output, string Error);
}
