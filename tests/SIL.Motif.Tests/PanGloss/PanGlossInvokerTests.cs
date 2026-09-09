using System.Text.Json;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The invocation module against the fake executable: argument shaping per subcommand, both streams
/// drained, admission and containment, the wall-clock cap, cancellation, and every failure as an outcome.
/// </summary>
public sealed class PanGlossInvokerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-invoker-" + Guid.NewGuid().ToString("N"));

    public PanGlossInvokerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    [Fact]
    public async Task Stats_ArgvIsStatsGrammarCacheThenForwardedInOrder_AndCompletedCarriesBothStreams()
    {
        var grammar = Project("stats");
        var cache = Path.Combine(_root, "stats.cache");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, cache, ["--group", "word", "--format", "jsonl"]),
            "test:stats", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.Contains("\"group\":\"word\"", completed.Output, StringComparison.Ordinal);
        Assert.Equal(["stats", grammar, "--cache", cache, "--group", "word", "--format", "jsonl"], Argv(grammar));
    }

    [Fact]
    public async Task Stats_ANonZeroExitIsRefusedWithItsStandardError()
    {
        var grammar = Project("stats-fail");
        FakeParser.Behave(_root, new { mode = "fail", exitCode = 3, standardError = "stats query exploded" });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "c"), []), "test:stats", CancellationToken.None);

        var refused = Assert.IsType<PanGlossOutcome.Refused>(outcome);
        Assert.Equal(3, refused.ExitCode);
        Assert.Contains("stats query exploded", refused.StandardError, StringComparison.Ordinal);
        Assert.Contains("pangloss stats exited 3", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALargeStandardErrorDoesNotDeadlock_BecauseBothStreamsAreDrainedBeforeWaiting()
    {
        var grammar = Project("stats-large");
        FakeParser.Behave(_root, new { mode = "fail", exitCode = 1, standardError = new string('x', 1 << 20) });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Stats(grammar, Path.Combine(_root, "c"), []), "test:stats", CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.IsType<PanGlossOutcome.Refused>(outcome);
    }

    [Fact]
    public async Task Import_ArgvIsImportThenTheTwoPaths_AndTheGrammarFileIsWritten()
    {
        var fwdata = Project("import");
        var grammarJson = Path.Combine(_root, "grammar.json");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(fwdata, grammarJson), "test:import", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Completed>(outcome);
        Assert.True(File.Exists(grammarJson));
        Assert.Equal(["import", fwdata, grammarJson], Argv(fwdata));
    }

    [Fact]
    public async Task Import_AZeroExitThatWroteNoGrammarIsIncomplete_NotCompleted()
    {
        var fwdata = Project("import-empty");
        FakeParser.Behave(_root, new { mode = "noReport", exitCode = 0 });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(fwdata, Path.Combine(_root, "g.json")), "test:import", CancellationToken.None);

        var incomplete = Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
        Assert.Contains("wrote no grammar", incomplete.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Batch_ArgvCarriesTheWordTimeoutOneThreadAndTheCacheWhenAsked_AndCompletedCarriesTheRows()
    {
        var project = Project("batch");
        var cache = Path.Combine(_root, "batch.cache");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa", "zzz"], TimeSpan.FromMilliseconds(1500), cache),
            "test:batch", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var rows = completed.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, rows.Length);
        Assert.StartsWith("0\tmotifa\t", rows[0], StringComparison.Ordinal);
        var argv = Argv(project);
        Assert.Equal("batch", argv[0]);
        Assert.Equal(project, argv[1]);
        Assert.EndsWith("words.txt", argv[2], StringComparison.Ordinal);
        Assert.EndsWith("out.tsv", argv[3], StringComparison.Ordinal);
        Assert.Equal(["--word-timeout-ms", "1500", "--threads", "1", "--stats", "--cache", cache], argv[4..]);
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public async Task Batch_WithoutACacheSendsNoStatsFlags()
    {
        var project = Project("batch-plain");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)), "test:batch", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "1000", "--threads", "1"], Argv(project)[4..]);
    }

    [Fact]
    public async Task Batch_AZeroExitThatWroteNoCacheIsIncomplete()
    {
        var project = Project("batch-nocache");
        FakeParser.Behave(_root, new { mode = "noReport", exitCode = 0 });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1), Path.Combine(_root, "never.cache")),
            "test:batch", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
    }

    [Fact]
    public async Task AnAbsentExecutableIsUnavailable_NotAnException()
    {
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executablePath: null, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(Project("absent"), Path.Combine(_root, "g.json")), "test", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains("MOTIF_PANGLOSS_EXE", unavailable.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExecutableThatWillNotStartIsUnavailable_NotAnException()
    {
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(Path.Combine(_root, "no-such-pangloss.exe"), queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(Project("nostart"), Path.Combine(_root, "g.json")), "test", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
    }

    [Fact]
    public async Task TheWallClockCapStopsAHungParser_AndReportsTimedOut()
    {
        var project = Project("hang");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:hang", CancellationToken.None,
            wallClockCap: TimeSpan.FromMilliseconds(400));

        var timedOut = Assert.IsType<PanGlossOutcome.TimedOut>(outcome);
        Assert.Equal(TimeSpan.FromMilliseconds(400), timedOut.Cap);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellationStopsTheParser_AndReportsCancelled()
    {
        var project = Project("cancel");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:cancel", cts.Token);

        Assert.IsType<PanGlossOutcome.Cancelled>(outcome);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task EveryLaunchRunsInsideTheAdmittedJobObject()
    {
        var project = Project("contained");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var queue = NewQueue();
        WindowsCpuJob? admitted = null;
        queue.JobAdmitted = job => admitted = job;
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath, queue);
        using var cts = new CancellationTokenSource();

        var run = invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:contained", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (admitted is null || admitted.QueryTotalProcessCount() == 0))
            await Task.Delay(20);

        Assert.NotNull(admitted);
        // The parser's console host joins the job beside it, so the count is at least one, never exactly one.
        Assert.True(admitted!.QueryTotalProcessCount() >= 1, "No process was assigned to the admitted job.");
        cts.Cancel();
        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
    }

    private PanGlossInvoker Invoker() => new(FakeParser.ExecutablePath, NewQueue());

    private static MachinePanGlossQueue NewQueue() => new(new[]
    {
        "Local\\MotifInvokerTests-" + Guid.NewGuid().ToString("N") + "-0",
        "Local\\MotifInvokerTests-" + Guid.NewGuid().ToString("N") + "-1",
    });

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "the fake parser never reads this.");
        return path;
    }

    private static string[] Argv(string besidePath) =>
        JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(besidePath)!, "_pangloss-argv.json")))!;

    private static async Task AssertStoppedTicking(string heartbeat)
    {
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await Task.Delay(200);
        var before = File.ReadAllText(heartbeat);
        await Task.Delay(250);
        Assert.Equal(before, File.ReadAllText(heartbeat));
    }
}
