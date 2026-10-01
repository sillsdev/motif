using System.Diagnostics;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class CancellationArgvTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-cli-cancellation-" + Guid.NewGuid().ToString("N"));
    private readonly string _workerRoot;
    private readonly PristineProjectFixture _pristine;

    public CancellationArgvTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        _workerRoot = Path.Combine(_root, "worker");
        Directory.CreateDirectory(_workerRoot);
    }

    [Fact]
    public async Task InterruptingStatsStopsTheParserAndReturnsItsCancellationRefusal()
    {
        var project = _pristine.CopyProjectFile();
        await CaptureBaseline(project);
        var selection = await CliProcess.RunAsync(_workerRoot, null, false, "selection", "set-default",
            "--project", project, "--name", "Default", "--add-words", "motifa", "--json");
        Assert.True(selection.ExitCode == 0, selection.FailureDetails);
        var assessment = await CliProcess.RunAsync(_workerRoot, null, false, "assess", project, "--json");
        Assert.True(assessment.ExitCode == 0, assessment.FailureDetails);

        var heartbeat = Path.Combine(_root, "stats-parser-heartbeat.txt");
        var processIdPath = Path.Combine(_root, "stats-parser-process-id.txt");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat, processIdPath });
        using var launched = InterruptibleCli.Start(CliProcess.CreateStartInfo(
            _workerRoot, parser, false, "stats", project, "--json"));

        var parserId = await WaitForParser(launched, heartbeat, processIdPath);
        Assert.True(launched.Interrupt(), "Could not send Ctrl+Break to the CLI process group.");
        await launched.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var error = launched.ReadStderr();
        Assert.True(launched.ExitCode == 2, $"Expected cancelled exit code 2.{Environment.NewLine}{error}");
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(error)!;
        Assert.Equal("stats.cancelled", failure.Code);
        Assert.Equal(FailureReason.Refused, failure.Reason);
        await AssertProcessStopped(parserId);
    }

    [Fact]
    public async Task InterruptingHandoffWithoutAnAssessmentStopsTheParserAndPublishesNothing()
    {
        var project = _pristine.CopyProjectFile();
        AddWordform(project, "handoff-cancellation-word");
        await CaptureBaseline(project);
        var selection = await CliProcess.RunAsync(_workerRoot, null, false, "selection", "set-default",
            "--project", project, "--name", "Default", "--add-words", "motifa", "--json");
        Assert.True(selection.ExitCode == 0, selection.FailureDetails);
        var heartbeat = Path.Combine(_root, "handoff-parser-heartbeat.txt");
        var processIdPath = Path.Combine(_root, "handoff-parser-process-id.txt");
        var parser = CopyFakeParser(new { heartbeatPath = heartbeat, processIdPath });
        var destination = Path.Combine(_root, "cancelled-handoff");
        using var launched = InterruptibleCli.Start(CliProcess.CreateStartInfo(
            _workerRoot, parser, false, "handoff", project, "--out", destination, "--no-assess", "--json"));

        var parserId = await WaitForParser(launched, heartbeat, processIdPath);
        Assert.True(launched.Interrupt(), "Could not send Ctrl+Break to the CLI process group.");
        await launched.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        var error = launched.ReadStderr();
        Assert.True(launched.ExitCode == 2, $"Expected cancelled exit code 2.{Environment.NewLine}{error}");
        var failure = ProjectionJson.Deserialize<FailureEnvelope>(error)!;
        Assert.Equal("handoff.cancelled", failure.Code);
        Assert.Equal(FailureReason.Cancelled, failure.Reason);
        Assert.False(Directory.Exists(destination));
        await AssertProcessStopped(parserId);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private async Task CaptureBaseline(string project)
    {
        var result = await CliProcess.RunAsync(_workerRoot, null, false,
            "baseline", "capture", project, "--json");
        Assert.True(result.ExitCode == 0, result.FailureDetails);
    }

    private async Task<int> WaitForParser(InterruptibleCli launched, string heartbeat, string processIdPath)
    {
        var process = launched.Process;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while ((!File.Exists(heartbeat) || !File.Exists(processIdPath)) &&
            !process.HasExited && DateTime.UtcNow < deadline)
            await Task.Delay(25);

        Assert.True(File.Exists(heartbeat) && File.Exists(processIdPath),
            $"The fake parser did not start.{Environment.NewLine}" +
            launched.ReadStderr() + launched.ReadStdout());
        Assert.False(process.HasExited, "The CLI exited before cancellation was sent.");
        return int.Parse(File.ReadAllText(processIdPath));
    }

    private string CopyFakeParser(object behavior)
    {
        var directory = Path.Combine(_root, "fake-pangloss-" + Guid.NewGuid().ToString("N"));
        var parser = FakeParser.Copy(directory);
        FakeParser.BehaveBesideExecutable(parser, behavior);
        return parser;
    }

    private static void AddWordform(string project, string word)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(project);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(word, cache.DefaultVernWs)));
        new FwDataProjectLoader().Save(cache);
    }

    private static async Task AssertProcessStopped(int processId)
    {
        try
        {
            using var parser = Process.GetProcessById(processId);
            await parser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(parser.HasExited);
        }
        catch (ArgumentException) { }
    }
}
