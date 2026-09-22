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
        Assert.Contains("\"orientation\":\"word\"", completed.Output, StringComparison.Ordinal);
        Assert.Equal(["stats", grammar, "--cache", cache, "--group", "word", "--format", "jsonl"], Argv(grammar));
    }

    [Fact]
    public async Task ChildEnvironmentDoesNotReceiveAnUnrelatedParentVariable()
    {
        var grammar = Project("minimal-environment");
        var sentinel = "MOTIF_TEST_SENTINEL_" + Guid.NewGuid().ToString("N");
        var previous = Environment.GetEnvironmentVariable(sentinel);
        Environment.SetEnvironmentVariable(sentinel, "must-not-cross-process-boundary");
        try
        {
            using var invoker = Invoker();
            var outcome = await invoker.RunAsync(
                new PanGlossRequest.Stats(grammar, Path.Combine(_root, "cache"), []),
                "test:minimal-environment", CancellationToken.None);

            Assert.IsType<PanGlossOutcome.Completed>(outcome);
            Assert.DoesNotContain(sentinel, EnvironmentNames(grammar));
        }
        finally
        {
            // The real process variable is the boundary this test is proving.
            Environment.SetEnvironmentVariable(sentinel, previous);
        }
    }

    [Fact]
    public async Task AWrongDescriptionRefusesBeforeTheFirstBatch()
    {
        var project = Project("wrong-description");
        var executable = FakeParser.CopyWithWrongDescription(Path.Combine(_root, "wrong-parser"));
        using var queue = NewQueue();
        using var invoker = new PanGlossInvoker(executable, queue);

        var outcome = await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:wrong-description", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Contains(executable, unavailable.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(project)!, "_pangloss-argv.json")));
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
        Assert.Equal(["--word-timeout-ms", "1500", "--step-cap", "200000", "--threads", "1", "--stats", "--cache", cache], argv[4..]);
        Assert.True(File.Exists(cache));
    }

    [Fact]
    public async Task Batch_WithoutACacheSendsNoStatsFlags()
    {
        var project = Project("batch-plain");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)), "test:batch", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "1000", "--step-cap", "200000", "--threads", "1"], Argv(project)[4..]);
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
    public async Task Batch_ExplicitStepBudgetIsIndependentOfTheTimeLimit()
    {
        var project = Project("batch-budget");
        using var invoker = Invoker();

        await invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromMilliseconds(700), PerWordStepLimit: 123),
            "test:batch-budget", CancellationToken.None);

        Assert.Equal(["--word-timeout-ms", "700", "--step-cap", "123", "--threads", "1"], Argv(project)[4..]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task Batch_RejectsNegativeStepBudgetsBeforeInvocation(int stepLimit)
    {
        var project = Project("batch-invalid-budget");
        using var invoker = Invoker();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => invoker.RunAsync(
            new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1), PerWordStepLimit: stepLimit),
            "test:batch-invalid-budget", CancellationToken.None));

        Assert.Equal("PerWordStepLimit", exception.ParamName);
        Assert.False(File.Exists(Path.Combine(_root, "_pangloss-argv.json")));
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
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var run = invoker.RunAsync(
            new PanGlossRequest.Import(project, Path.Combine(_root, "g.json")), "test:cancel", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await cts.CancelAsync();
        var outcome = await run;

        Assert.IsType<PanGlossOutcome.Cancelled>(outcome);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellingBatchWaitsForArtifactHandlesToCloseBeforeCleanup()
    {
        var project = Project("cancel-batch");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = invoker.RunAsync(new PanGlossRequest.Batch(project, ["motifa"], TimeSpan.FromSeconds(1)),
            "test:cancel-batch", cancellation.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.True(File.Exists(heartbeat));
        var wordsPath = Argv(project)[2];
        Assert.Throws<IOException>(() => File.Open(wordsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        await cancellation.CancelAsync();
        Assert.IsType<PanGlossOutcome.Cancelled>(await run);
        Assert.False(Directory.Exists(Path.GetDirectoryName(wordsPath)));
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

    [Theory]
    [InlineData("source.fwdata")]
    [InlineData("words.txt")]
    public async Task CapturedBatchRefusesChangedStagedInputAndRemovesUnpublishedArtifacts(string changedFile)
    {
        var source = Project("changed");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var outcome = await PanGlossInvoker.LaunchAsync(FakeParser.ExecutablePath,
            new PanGlossRequest.Batch(source, ["motifa"], TimeSpan.FromSeconds(1), ArtifactDirectory: artifacts),
            _ => File.WriteAllText(Path.Combine(artifacts, changedFile), "changed while parsing"),
            TimeSpan.FromSeconds(10), CancellationToken.None);

        var refused = Assert.IsType<PanGlossOutcome.Incomplete>(outcome);
        Assert.Contains("changed during", refused.Detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(artifacts));
        Assert.Equal("the fake parser never reads this.", File.ReadAllText(source));
    }

    [Fact]
    public async Task CapturedBatchRetainsExactInputsAndArtifactsAfterTheInvokerReturns()
    {
        var source = Project("captured");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(
            source, ["motifa"], TimeSpan.FromMilliseconds(700), PerWordStepLimit: 123,
            ArtifactDirectory: artifacts), "test:capture", CancellationToken.None);

        var completed = Assert.IsType<PanGlossOutcome.Completed>(outcome);
        var evidence = Assert.IsType<BatchInvocationEvidence>(completed.BatchEvidence);
        Assert.NotEqual(source, evidence.SourcePath);
        Assert.Equal(File.ReadAllBytes(source), File.ReadAllBytes(evidence.SourcePath));
        Assert.Equal(BatchInvocationEvidence.DigestFile(source), evidence.SourceBytesSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(FakeParser.ExecutablePath), evidence.ExecutableBytesSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(evidence.WordsPath), evidence.WordsSha256);
        Assert.Equal(BatchInvocationEvidence.DigestFile(evidence.TsvPath), evidence.TsvSha256);
        Assert.Equal(completed.Output, File.ReadAllText(evidence.TsvPath));
        Assert.Equal(completed.StandardError, File.ReadAllText(evidence.StandardErrorPath));
        Assert.Equal(700, evidence.PerWordTimeoutMs);
        Assert.Equal(123, evidence.PerWordStepLimit);
        Assert.Equal(1, evidence.Threads);
        Assert.False(evidence.CollectStatistics);
        File.WriteAllText(source, "changed after invocation");
        Assert.Equal(evidence.SourceBytesSha256, BatchInvocationEvidence.DigestFile(evidence.SourcePath));
    }

    [Fact]
    public async Task CapturedBatchRefusesToOverwriteAnExistingArtifactDirectory()
    {
        var source = Project("existing");
        var artifacts = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(artifacts);
        var sentinel = Path.Combine(artifacts, "keep.txt");
        File.WriteAllText(sentinel, "existing evidence");
        using var invoker = Invoker();

        var outcome = await invoker.RunAsync(new PanGlossRequest.Batch(
            source, ["motifa"], TimeSpan.FromSeconds(1), ArtifactDirectory: artifacts),
            "test:existing", CancellationToken.None);

        Assert.IsType<PanGlossOutcome.Unavailable>(outcome);
        Assert.Equal("existing evidence", File.ReadAllText(sentinel));
    }

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

    private static string[] EnvironmentNames(string besidePath) =>
        JsonSerializer.Deserialize<string[]>(
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(besidePath)!, "_pangloss-environment.json")))!;

    private static async Task AssertStoppedTicking(string heartbeat)
    {
        Assert.True(File.Exists(heartbeat), "The fake parser never started ticking.");
        await Task.Delay(200);
        var before = File.ReadAllText(heartbeat);
        await Task.Delay(250);
        Assert.Equal(before, File.ReadAllText(heartbeat));
    }
}
