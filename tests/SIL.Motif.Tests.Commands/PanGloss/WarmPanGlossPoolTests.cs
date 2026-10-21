using System.Diagnostics;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// The resident parser pool against the fake <c>pangloss serve</c>: warm answers equal cold ones, prewarm is idempotent,
/// idle sessions are evicted, a cancel kills and the next word restarts, the ceiling kills, an unsupported parser
/// falls back to cold, a changed project token replaces the session, and a failed load falls back, then retries.
/// </summary>
public sealed class WarmPanGlossPoolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-warm-" + Guid.NewGuid().ToString("N"));
    private readonly List<IDisposable> _disposables = [];

    public WarmPanGlossPoolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var disposable in _disposables) disposable.Dispose();
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private WarmPanGlossPool Pool(WarmPanGlossOptions? options = null, string? executable = null)
    {
        var queue = new MachinePanGlossQueue(
        [
            "Local\\MotifWarmTests-" + Guid.NewGuid().ToString("N") + "-0",
            "Local\\MotifWarmTests-" + Guid.NewGuid().ToString("N") + "-1",
        ]);
        var pool = new WarmPanGlossPool(executable ?? FakeParser.ExecutablePath, queue, ownsQueue: true, options);
        _disposables.Add(pool);
        return pool;
    }

    private (string Grammar, string StartLog, string RequestLog) Project(string name, object? extra = null)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        var grammar = Path.Combine(directory, "p.fwdata");
        File.WriteAllText(grammar, "the fake parser never reads this.");
        var startLog = Path.Combine(directory, "starts.log");
        var requestLog = Path.Combine(directory, "requests.log");
        WriteBehaviour(grammar, startLog, requestLog, extra);
        return (grammar, startLog, requestLog);
    }

    private static void WriteBehaviour(string grammar, string startLog, string requestLog, object? extra)
    {
        var values = new Dictionary<string, object?> { ["ServeStartLog"] = startLog, ["ServeRequestLog"] = requestLog };
        if (extra is not null)
            foreach (var property in extra.GetType().GetProperties()) values[property.Name] = property.GetValue(extra);
        FakeParser.Behave(Path.GetDirectoryName(grammar)!, values);
    }

    private static string[] Lines(string path) => File.Exists(path) ? File.ReadAllLines(path) : [];

    private static bool Alive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task Eventually(Func<bool> condition, string because)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Timed out waiting for " + because);
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task AWarmAnswerIsByteForByteTheColdAnswer()
    {
        var (grammar, _, _) = Project("equal");
        using var pool = Pool();
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath,
            new MachinePanGlossQueue(["Local\\MotifWarmCold-" + Guid.NewGuid().ToString("N")]));

        var warm = Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        var cold = Assert.IsType<PanGlossOutcome.Completed>(
            await invoker.RunAsync(new PanGlossRequest.Trace(grammar, "motifa"), "cold", CancellationToken.None));

        Assert.Equal(cold.Output.Trim(), warm.Json);
    }

    [Fact]
    public async Task PrewarmReturnsAtOnceLoadsOnceAndIsIdempotent()
    {
        var (grammar, startLog, _) = Project("prewarm", new { ServeLoadDelayMs = 600 });
        using var pool = Pool();
        var changes = new List<WarmSessionPhase>();
        pool.StateChanged += (_, args) => { lock (changes) changes.Add(args.State.Phase); };

        var clock = Stopwatch.StartNew();
        var first = pool.Prewarm(grammar, "t1", grammar);
        var second = pool.Prewarm(grammar, "t1", grammar);
        Assert.True(clock.ElapsedMilliseconds < 500, "Prewarm must not wait for the load.");
        Assert.False(first.IsCompleted);
        Assert.Equal(WarmSessionPhase.Loading, pool.GetState(grammar).Phase);

        Assert.Equal(WarmSessionState.Ready, await first);
        Assert.Equal(WarmSessionState.Ready, await second);
        Assert.Equal(WarmSessionState.Ready, await pool.Prewarm(grammar, "t1", grammar));
        await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None);

        Assert.Single(Lines(startLog));
        await Eventually(() => { lock (changes) return changes.Contains(WarmSessionPhase.Ready); }, "the Ready event");
        lock (changes) Assert.Equal([WarmSessionPhase.Loading, WarmSessionPhase.Ready], changes.Distinct());
    }

    [Fact]
    public async Task AWordSubmittedBeforeReadyWaitsForTheLoadAndACancelOnlyAbandonsTheWait()
    {
        var (grammar, startLog, _) = Project("waits", new { ServeLoadDelayMs = 700 });
        using var pool = Pool();
        _ = pool.Prewarm(grammar, "t1", grammar);

        using var cancel = new CancellationTokenSource(100);
        var abandoned = await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, cancel.Token);
        Assert.IsType<WarmParseResult.Cancelled>(abandoned);

        var waited = await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None);
        Assert.IsType<WarmParseResult.Completed>(waited);
        Assert.Single(Lines(startLog));
    }

    [Fact]
    public async Task ConcurrentWordsOnOneSessionAllAnswerInOrderOfTheirOwnWord()
    {
        var (grammar, startLog, requestLog) = Project("concurrent", new { ServeRequestDelayMs = 20 });
        using var pool = Pool();

        var words = Enumerable.Range(0, 6).Select(index => "w" + index).ToArray();
        var results = await Task.WhenAll(words.Select(word =>
            pool.TryWordAsync(grammar, "t1", grammar, word, true, CancellationToken.None)));

        for (var index = 0; index < words.Length; index++)
            Assert.Contains($"\"word\":\"{words[index]}\"", Assert.IsType<WarmParseResult.Completed>(results[index]).Json);
        Assert.Single(Lines(startLog));
        Assert.Equal(words.Length, Lines(requestLog).Length);
    }

    [Fact]
    public async Task AnIdleSessionIsEvictedAndTheNextWordStartsANewOne()
    {
        var (grammar, startLog, _) = Project("idle");
        using var pool = Pool(new WarmPanGlossOptions { IdleTimeout = TimeSpan.FromMilliseconds(400) });
        await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None);
        var pid = int.Parse(Lines(startLog)[0]);
        Assert.True(Alive(pid));

        await Eventually(() => pool.GetState(grammar).Phase == WarmSessionPhase.Idle, "idle eviction");
        await Eventually(() => !Alive(pid), "the evicted process to exit");

        Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal(2, Lines(startLog).Length);
    }

    [Fact]
    public async Task ACancelKillsTheProcessAndTheNextWordRestartsIt()
    {
        var (grammar, startLog, _) = Project("cancel");
        using var pool = Pool();
        await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None);
        var pid = int.Parse(Lines(startLog)[0]);

        using var cancel = new CancellationTokenSource();
        var hung = pool.TryWordAsync(grammar, "t1", grammar, "hang", true, cancel.Token);
        await Task.Delay(500);
        cancel.Cancel();
        Assert.IsType<WarmParseResult.Cancelled>(await hung);
        await Eventually(() => !Alive(pid), "the cancelled process to be killed");
        Assert.Equal(WarmSessionPhase.Idle, pool.GetState(grammar).Phase);

        Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal(2, Lines(startLog).Length);
    }

    [Fact]
    public async Task AParserPastTheMemoryCeilingIsKilledAndTheWordFallsBackToCold()
    {
        var (grammar, startLog, _) = Project("ceiling", new { ServeAllocateBytes = 900 * 1024 * 1024 });
        using var pool = Pool(new WarmPanGlossOptions { MemoryCeilingBytes = 300UL * 1024 * 1024 });

        await pool.Prewarm(grammar, "t1", grammar);
        await Eventually(() => pool.GetState(grammar).Phase == WarmSessionPhase.Failed, "the ceiling kill");

        var state = pool.GetState(grammar);
        Assert.NotNull(state.FailureCode);
        Assert.NotNull(state.FailureMessage);
        if (!OperatingSystem.IsWindows()) Assert.Equal("memory-ceiling", state.FailureCode);
        var fallback = Assert.IsType<WarmParseResult.Fallback>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal(state.FailureCode, fallback.Code);
        await Eventually(() => !Alive(int.Parse(Lines(startLog)[0])), "the killed process to be gone");
    }

    [Fact]
    public async Task AParserWithoutServeIsFailedWithACodeAndEveryWordFallsBackToCold()
    {
        var executable = FakeParser.Copy(Path.Combine(_root, "no-serve-parser"));
        FakeParser.OmitDescribeEntries(executable, "serve");
        var (grammar, startLog, _) = Project("no-serve");
        using var pool = Pool(executable: executable);

        var state = await pool.Prewarm(grammar, "t1", grammar);

        Assert.Equal(WarmSessionPhase.Failed, state.Phase);
        Assert.Equal("serve-unsupported", state.FailureCode);
        var fallback = Assert.IsType<WarmParseResult.Fallback>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal("serve-unsupported", fallback.Code);
        Assert.Empty(Lines(startLog));
    }

    [Fact]
    public async Task AChangedProjectTokenInvalidatesTheSessionAndStartsAnother()
    {
        var (grammar, startLog, _) = Project("token");
        using var pool = Pool();
        await pool.TryWordAsync(grammar, "baseline-1", grammar, "motifa", true, CancellationToken.None);
        var first = int.Parse(Lines(startLog)[0]);

        Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "baseline-2", grammar, "motifa", true, CancellationToken.None));

        Assert.Equal(2, Lines(startLog).Length);
        await Eventually(() => !Alive(first), "the invalidated session's process to exit");
    }

    [Fact]
    public async Task AFatalLoadIsFailedWithItsCodeFallsBackThenRetriesAfterTheCooldownAndOnPrewarm()
    {
        var (grammar, startLog, requestLog) = Project("fatal", new { ServeFatalCode = "bad-grammar" });
        using var pool = Pool(new WarmPanGlossOptions { RetryCooldown = TimeSpan.FromMilliseconds(700) });

        var state = await pool.Prewarm(grammar, "t1", grammar);
        Assert.Equal(WarmSessionPhase.Failed, state.Phase);
        Assert.Equal("bad-grammar", state.FailureCode);
        Assert.Contains("fake serve", state.FailureMessage);
        var fallback = Assert.IsType<WarmParseResult.Fallback>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal("bad-grammar", fallback.Code);
        Assert.Single(Lines(startLog));

        WriteBehaviour(grammar, startLog, requestLog, null);
        await Task.Delay(800);
        Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
        Assert.Equal(2, Lines(startLog).Length);

        WriteBehaviour(grammar, startLog, requestLog, new { ServeFatalCode = "again" });
        using var second = Pool();
        Assert.Equal(WarmSessionPhase.Failed, (await second.Prewarm(grammar, "t1", grammar)).Phase);
        WriteBehaviour(grammar, startLog, requestLog, null);
        Assert.Equal(WarmSessionState.Ready, await second.Prewarm(grammar, "t1", grammar));
    }

    [Fact]
    public async Task ACrashOnLoadAndALoadThatNeverFinishesBothEndFailedNotLoading()
    {
        var (crash, _, _) = Project("crash", new { ServeCrashOnLoad = true });
        using var pool = Pool();
        var crashed = await pool.Prewarm(crash, "t1", crash);
        Assert.Equal(WarmSessionPhase.Failed, crashed.Phase);
        Assert.Equal("process-exited", crashed.FailureCode);

        var (slow, slowLog, _) = Project("slow", new { ServeLoadDelayMs = 30_000 });
        using var impatient = Pool(new WarmPanGlossOptions { LoadTimeout = TimeSpan.FromMilliseconds(800) });
        var timedOut = await impatient.Prewarm(slow, "t1", slow);
        Assert.Equal("load-timeout", timedOut.FailureCode);
        Assert.Equal(WarmSessionPhase.Failed, impatient.GetState(slow).Phase);
        await Eventually(() => !Alive(int.Parse(Lines(slowLog)[0])), "the slow loader to be killed");
    }

    [Fact]
    public async Task ARejectedWordFallsBackToColdAndLeavesTheSessionReady()
    {
        var (grammar, _, _) = Project("error");
        using var pool = Pool();

        var fallback = Assert.IsType<WarmParseResult.Fallback>(
            await pool.TryWordAsync(grammar, "t1", grammar, "boom", true, CancellationToken.None));

        Assert.Equal("parse-failed", fallback.Code);
        Assert.Equal(WarmSessionPhase.Ready, pool.GetState(grammar).Phase);
        Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None));
    }

    [Fact]
    public async Task TheServeExitCodeIsPassedThroughAndReadLikeTheColdProcessesExitCode()
    {
        var (stopped, _, _) = Project("exit-capped", new { ServeExitCode = 1, TraceCapped = true });
        var (refused, _, _) = Project("exit-refused", new { ServeExitCode = 3 });
        using var pool = Pool();
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath,
            new MachinePanGlossQueue(["Local\\MotifWarmExit-" + Guid.NewGuid().ToString("N")]));
        var tracer = new WarmFirstTracer(pool, new PanGlossTracer(invoker));

        var raw = Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(stopped, "t1", stopped, "motifa", true, CancellationToken.None));
        Assert.Equal(1, raw.ExitCode);

        // A nonzero exit whose document reports a stop is an incomplete trace, as it is from the cold process.
        Assert.IsType<PanGlossTraceOutcome.Incomplete>(
            await tracer.TraceAsync(stopped, "t1", stopped, "motifa", CancellationToken.None));
        // Any other nonzero exit is not an answer: the cold path decides (here it exits 0 and completes).
        Assert.Equal(3, Assert.IsType<WarmParseResult.Completed>(
            await pool.TryWordAsync(refused, "t1", refused, "motifa", true, CancellationToken.None)).ExitCode);
        Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(refused, "t1", refused, "motifa", CancellationToken.None));
    }

    [Fact]
    public async Task AWordWithADifferentStepCapGetsItsOwnSessionStartedWithThatCap()
    {
        var (grammar, startLog, _) = Project("caps", new { ServeArgvLog = Path.Combine(_root, "caps", "argv.log") });
        var argvLog = Path.Combine(_root, "caps", "argv.log");
        using var pool = Pool();

        await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None);
        await pool.TryWordAsync(grammar, "t1", grammar, "motifa", true, CancellationToken.None, new StepCap(1000));
        await pool.TryWordAsync(grammar, "t1", grammar, "motifb", true, CancellationToken.None, new StepCap(1000));
        await pool.TryWordAsync(grammar, "t1", grammar, "motifc", true, CancellationToken.None, StepCap.Unbounded);

        var argv = Lines(argvLog);
        Assert.Equal(3, Lines(startLog).Length);
        Assert.DoesNotContain("--step-cap", argv[0]);
        Assert.Contains("\"--step-cap\",\"1000\"", argv[1]);
        Assert.Contains("\"--step-cap\",\"unbounded\"", argv[2]);
        Assert.Equal(WarmSessionPhase.Ready, pool.GetState(grammar).Phase);
        Assert.Equal(WarmSessionPhase.Ready, pool.GetState(grammar, new StepCap(1000)).Phase);
        Assert.Equal(WarmSessionPhase.Idle, pool.GetState(grammar, new StepCap(7)).Phase);

        pool.Evict(grammar);
        Assert.Equal(WarmSessionPhase.Idle, pool.GetState(grammar, new StepCap(1000)).Phase);
    }

    [Fact]
    public async Task AnUnterminatedFinalLineIsDiscardedAndTheWordFallsBackToCold()
    {
        var (grammar, _, _) = Project("partial");
        using var pool = Pool();
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath,
            new MachinePanGlossQueue(["Local\\MotifWarmPartial-" + Guid.NewGuid().ToString("N")]));
        var tracer = new WarmFirstTracer(pool, new PanGlossTracer(invoker));

        var fallback = Assert.IsType<WarmParseResult.Fallback>(
            await pool.TryWordAsync(grammar, "t1", grammar, "partial", true, CancellationToken.None));
        Assert.Equal("process-exited", fallback.Code);

        // The cold path answers the word, and the next call after the cooldown-free retry still works.
        var outcome = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(grammar, "t1", grammar, "partial", CancellationToken.None));
        Assert.Equal("partial", outcome.Word);
    }

    [Fact]
    public async Task TheWarmFirstTracerAnswersLikeTheColdOneAndFallsBackToItWhenTheSessionFailed()
    {
        var (grammar, _, _) = Project("tracer");
        var (broken, _, _) = Project("tracer-broken", new { ServeFatalCode = "bad-grammar" });
        using var pool = Pool();
        using var invoker = new PanGlossInvoker(FakeParser.ExecutablePath,
            new MachinePanGlossQueue(["Local\\MotifWarmTracer-" + Guid.NewGuid().ToString("N")]));
        var cold = new PanGlossTracer(invoker);
        var tracer = new WarmFirstTracer(pool, cold);

        var viaCold = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await cold.TraceAsync(grammar, "motifa", CancellationToken.None));
        var viaWarm = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(grammar, "t1", grammar, "motifa", CancellationToken.None));
        var viaFallback = Assert.IsType<PanGlossTraceOutcome.Completed>(
            await tracer.TraceAsync(broken, "t1", broken, "motifa", CancellationToken.None));

        Assert.Equal(viaCold.Summary, viaWarm.Summary);
        Assert.Equal(viaCold.Document!.Signature, viaWarm.Document!.Signature);
        Assert.Equal("motifa", viaFallback.Word);
    }
}
