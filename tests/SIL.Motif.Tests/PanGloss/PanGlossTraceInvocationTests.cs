using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// <see cref="PanGlossTracer"/> over a real process boundary — the fake executable, not a stub — proving the
/// argv PanGlossRequest.Trace builds, and that a hang or a cancellation during a real trace still comes back
/// as a typed outcome through the same single invocation path every other request uses.
/// </summary>
public sealed class PanGlossTraceInvocationTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-trace-invoker-" + Guid.NewGuid().ToString("N"));

    public PanGlossTraceInvocationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    private const string GoldenTraceJson =
        "{\"type\":\"WordAnalysis\",\"inputShape\":\"sagd\",\"children\":[" +
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}]}";

    [Fact]
    public async Task Trace_ArgvAsksForTheDetailedJson_AndCompletedCarriesTheTreeSummaryAndDetails()
    {
        var grammar = Project("trace");
        FakeParser.Behave(_root, new { traceSignature = "32+PAST|sag+?d", traceJson = GoldenTraceJson });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);

        var outcome = await tracer.TraceAsync(grammar, "sagd", CancellationToken.None);

        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.Equal("32+PAST|sag+?d", completed.Summary.Signature);
        Assert.Equal(2, completed.Summary.StepCount);
        Assert.NotNull(completed.Tree);
        Assert.Equal(42, completed.Details!.Steps);
        Assert.Equal(
            ["parse", grammar, "sagd", "--trace", "--trace-format", "json", "--trace-details"], Argv(grammar));
    }

    [Fact]
    public async Task ATraceTheParserCappedIsIncomplete_KeepingTheTreeAndSayingWhy()
    {
        var grammar = Project("trace-capped");
        FakeParser.Behave(_root, new { traceSignature = "-", traceJson = GoldenTraceJson, traceCapped = true });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);

        var outcome = await tracer.TraceAsync(grammar, "sagd", CancellationToken.None);

        var incomplete = Assert.IsType<PanGlossTraceOutcome.Incomplete>(outcome);
        Assert.NotNull(incomplete.Tree);
        Assert.False(incomplete.Summary!.Completed);
        Assert.True(incomplete.Details!.Capped);
        Assert.Contains("step cap after 42 steps", incomplete.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeclinedWordMapsToDeclined_WithTheParsersOwnStandardError()
    {
        var grammar = Project("trace-decline");
        FakeParser.Behave(_root, new { mode = "fail", exitCode = 1, standardError = "read golden.xml: not found" });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);

        var outcome = await tracer.TraceAsync(grammar, "sagd", CancellationToken.None);

        var declined = Assert.IsType<PanGlossTraceOutcome.Declined>(outcome);
        Assert.Contains("read golden.xml: not found", declined.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheWallClockCapStopsAHungTrace_AndReportsIncomplete()
    {
        var grammar = Project("trace-hang");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);

        var outcome = await tracer.TraceAsync(
            grammar, "sagd", CancellationToken.None, timeout: TimeSpan.FromMilliseconds(400));

        var incomplete = Assert.IsType<PanGlossTraceOutcome.Incomplete>(outcome);
        Assert.Null(incomplete.Tree);
        Assert.Contains("did not finish", incomplete.Reason, StringComparison.Ordinal);
        await AssertStoppedTicking(heartbeat);
    }

    [Fact]
    public async Task CancellationStopsTheTrace_AndReportsCancelled()
    {
        var grammar = Project("trace-cancel");
        var heartbeat = Path.Combine(_root, "heartbeat.txt");
        FakeParser.Behave(_root, new { heartbeatPath = heartbeat });
        using var invoker = Invoker();
        var tracer = new PanGlossTracer(invoker);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var run = tracer.TraceAsync(grammar, "sagd", cts.Token);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!File.Exists(heartbeat) && !run.IsCompleted && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        await cts.CancelAsync();
        var outcome = await run;

        Assert.IsType<PanGlossTraceOutcome.Cancelled>(outcome);
        await AssertStoppedTicking(heartbeat);
    }

    private PanGlossInvoker Invoker() => new(FakeParser.ExecutablePath, NewQueue());

    private static MachinePanGlossQueue NewQueue() => new(new[]
    {
        "Local\\MotifTraceInvokerTests-" + Guid.NewGuid().ToString("N") + "-0",
        "Local\\MotifTraceInvokerTests-" + Guid.NewGuid().ToString("N") + "-1",
    });

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "the fake parser never reads this.");
        return path;
    }

    private static string[] Argv(string besidePath) =>
        System.Text.Json.JsonSerializer.Deserialize<string[]>(
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
