using System.Text.Json;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
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
        var baseline = await CliProcess.RunAsync(_workerRoot, null, false,
            "baseline", "capture", project, "--json");
        Assert.True(baseline.ExitCode == 0, baseline.FailureDetails);
        var selection = await CliProcess.RunAsync(_workerRoot, null, false, "selection", "set-default", "--project", project,
            "--name", "Default", "--add-words", "motifa", "--json");
        Assert.True(selection.ExitCode == 0, selection.FailureDetails);

        var parser = CopyFakeParser();
        var assess = await CliProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--json");
        Assert.True(assess.ExitCode == 0, assess.FailureDetails);
        var assessed = ProjectionJson.Deserialize<AssessCommandResponse>(assess.Output)!;
        Assert.NotEmpty(assessed.InvocationId);

        var destination = Path.Combine(_root, "handoff");
        var handoffResult = await CliProcess.RunAsync(_workerRoot, null, false, "handoff", project, "--out", destination,
            "--invocation", assessed.InvocationId, "--json");

        Assert.True(handoffResult.ExitCode == 0, handoffResult.FailureDetails);
        var handoff = ProjectionJson.Deserialize<HandoffCommandResponse>(handoffResult.Output)!;
        Assert.Equal(Path.GetFullPath(destination), Path.GetFullPath(handoff.OutputDirectory));
        Assert.Equal(assessed.InvocationId, handoff.InvocationId);
        Assert.Equal(assessed.AssessmentIds.Order(StringComparer.Ordinal), handoff.AssessmentIds.Order(StringComparer.Ordinal));
        Assert.Equal(5, handoff.Files.Count);
        Assert.Equal(new[]
        {
            "grammar.json",
            "handoff.md",
            "parse-results.json",
            "read_results.py",
            "texts.json",
        }, handoff.Files);
        Assert.All(handoff.Files, file => Assert.True(File.Exists(Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar))), file));
        using var assessmentDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(destination, "parse-results.json")));
        Assert.Equal("motifa", Assert.Single(assessmentDocument.RootElement.EnumerateArray())
            .GetProperty("word").GetString());

        var fullDestination = Path.Combine(_root, "full-folder");
        Directory.CreateDirectory(fullDestination);
        var keep = Path.Combine(fullDestination, "keep.txt");
        File.WriteAllText(keep, "preserve");
        var full = await CliProcess.RunAsync(_workerRoot, null, false, "handoff", project, "--out", fullDestination,
            "--invocation", assessed.InvocationId, "--json");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.InvalidArgument), full.ExitCode);
        Assert.Equal("handoff.destination-exists", ProjectionJson.Deserialize<FailureEnvelope>(full.Error)!.Code);
        Assert.Equal("preserve", File.ReadAllText(keep));
        Assert.Single(Directory.GetFiles(fullDestination));

        var missingDestination = Path.Combine(_root, "missing-run");
        var missing = await CliProcess.RunAsync(_workerRoot, null, false, "handoff", project, "--out", missingDestination,
            "--invocation", "missing-invocation", "--json");
        Assert.Equal(FailureEnvelope.ExitCodeFor(FailureReason.NotFound), missing.ExitCode);
        Assert.Equal("handoff.invocation-not-found", ProjectionJson.Deserialize<FailureEnvelope>(missing.Error)!.Code);
        Assert.False(Directory.Exists(missingDestination));
    }

    [Fact]
    public async Task HandoffReadsTheCapturedBaselineWhileAnotherProcessHoldsItOpen()
    {
        var project = _pristine.CopyProjectFile();
        var selection = await CliProcess.RunAsync(_workerRoot, null, false, "selection", "set-default",
            "--project", project, "--name", "Default", "--add-words", "motifa", "--json");
        Assert.True(selection.ExitCode == 0, selection.FailureDetails);
        var parser = CopyFakeParser();
        var assessment = await CliProcess.RunAsync(_workerRoot, parser, false, "assess", project, "--json");
        Assert.True(assessment.ExitCode == 0, assessment.FailureDetails);
        var assessed = ProjectionJson.Deserialize<AssessCommandResponse>(assessment.Output)!;
        var original = File.ReadAllBytes(assessed.Baseline.FwDataPath);
        using var held = new FwDataProjectLoader().LoadScratchCache(assessed.Baseline.FwDataPath);
        var heldPath = held.ProjectId.Path;
        Assert.Equal(Path.GetFullPath(assessed.Baseline.FwDataPath), Path.GetFullPath(heldPath));
        var destination = Path.Combine(_root, "held-baseline-handoff");

        var result = await CliProcess.RunAsync(_workerRoot, null, false,
            "handoff", project, "--out", destination, "--invocation", assessed.InvocationId, "--json");

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        var handoff = ProjectionJson.Deserialize<HandoffCommandResponse>(result.Output)!;
        Assert.Equal(Path.GetFullPath(assessed.Baseline.FwDataPath), Path.GetFullPath(handoff.Baseline.FwDataPath));
        Assert.Equal(heldPath, held.ProjectId.Path);
        Assert.Equal(original, File.ReadAllBytes(assessed.Baseline.FwDataPath));
        Assert.True(File.Exists(Path.Combine(destination, "texts.json")));
    }

    [Fact]
    public async Task BaselineOnlyHandoffReadsTheCapturedBaselineWhileAnotherProcessHoldsItOpen()
    {
        var project = _pristine.CopyProjectFile();
        Guid textId;
        using (var cache = new FwDataProjectLoader().LoadScratchCache(project))
        {
            textId = SeededProject.SeedText(cache, _pristine.Seed).TextId;
            new FwDataProjectLoader().Save(cache);
        }
        var capture = await CliProcess.RunAsync(_workerRoot, null, false,
            "baseline", "capture", project, "--json");
        Assert.True(capture.ExitCode == 0, capture.FailureDetails);
        var baseline = ProjectionJson.Deserialize<BaselineCaptureResponse>(capture.Output)!;
        var original = File.ReadAllBytes(baseline.FwDataPath);
        using var held = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
        var heldPath = held.ProjectId.Path;
        Assert.Equal(Path.GetFullPath(baseline.FwDataPath), Path.GetFullPath(heldPath));
        var destination = Path.Combine(_root, "held-baseline-only-handoff");

        var result = await CliProcess.RunAsync(_workerRoot, null, false,
            "handoff", project, "--out", destination, "--no-assess", "--texts", textId.ToString("D"), "--json");

        Assert.True(result.ExitCode == 0, result.FailureDetails);
        var handoff = ProjectionJson.Deserialize<HandoffCommandResponse>(result.Output)!;
        Assert.Equal(Path.GetFullPath(baseline.FwDataPath), Path.GetFullPath(handoff.Baseline.FwDataPath));
        Assert.Equal(heldPath, held.ProjectId.Path);
        Assert.Equal(original, File.ReadAllBytes(baseline.FwDataPath));
        Assert.True(File.Exists(Path.Combine(destination, "texts.json")));
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

}
