using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>
/// <see cref="PanGlossTracer"/> against a stubbed <see cref="IPanGlossInvoker"/>: the JSON parsing and
/// summary derivation this module owns, exercised with no process at all. Process mechanics — argv,
/// cancellation, a genuine wall-clock kill — belong to <c>PanGlossTraceInvocationTests</c>, which drives the
/// same module through the fake executable.
/// </summary>
public sealed class PanGlossTracerTests
{
    /// The tree a real capture against PanGloss's own <c>trace_render.rs</c> golden grammar and word printed.
    private const string GoldenTree =
        "{\"type\":\"WordAnalysis\",\"inputShape\":\"sagd\",\"children\":[" +
        "{\"type\":\"StratumAnalysisInput\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"StratumAnalysisOutput\",\"source\":\"S\",\"outputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sag\",\"children\":[" +
        "{\"type\":\"StratumAnalysisOutput\",\"source\":\"S\",\"outputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"StratumSynthesisInput\",\"source\":\"S\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\",\"subrule\":0,\"outputShape\":\"sagd\",\"children\":[" +
        "{\"type\":\"StratumSynthesisOutput\",\"source\":\"S\",\"outputShape\":\"sagd\",\"children\":[]}," +
        "{\"type\":\"Successful\",\"outputShape\":\"sagd\",\"children\":[]}]}," +
        "{\"type\":\"MorphologicalRuleSynthesis\",\"source\":\"ed_suffix\"," +
        "\"failureReason\":\"NonPartialRuleProhibitedAfterFinalTemplate\",\"inputShape\":\"sag\",\"children\":[]}," +
        "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"sag\",\"children\":[]}]}," +
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}]}";

    private static readonly string GoldenStandardOutput = TraceEnvelope.Of("32+PAST|sag+?d", GoldenTree);

    [Fact]
    public async Task CompletedTrace_ParsesTheVerbatimTreeAndDerivesTheSummary()
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.FromMilliseconds(4)) };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.Equal("sagd", completed.Word);
        Assert.NotNull(completed.Tree);
        var tree = completed.Tree!;
        Assert.Equal("WordAnalysis", tree.Type);
        Assert.Equal("sagd", tree.InputShape);
        Assert.Equal(4, tree.Children.Count);

        var summary = completed.Summary;
        Assert.Equal("32+PAST|sag+?d", summary.Signature);
        Assert.Equal(13, summary.StepCount);
        Assert.True(summary.Completed);
        Assert.Equal(["NonPartialRuleProhibitedAfterFinalTemplate", "PartialParse"], summary.FailureReasons);
        Assert.Equal("ed_suffix", summary.DeepestRuleReached);
    }

    [Fact]
    public async Task AWordWithNoTraceableShapeIsCompletedWithAnEmptyTree()
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Completed(TraceEnvelope.Of("-", null, invalidShape: true).Replace("sagd", "zagz", StringComparison.Ordinal), string.Empty, TimeSpan.FromMilliseconds(1)) };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "zagz", CancellationToken.None);

        var completed = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome);
        Assert.Null(completed.Tree);
        Assert.Equal("-", completed.Summary.Signature);
        Assert.Equal(0, completed.Summary.StepCount);
        Assert.True(completed.Summary.Completed);
        Assert.Empty(completed.Summary.FailureReasons);
        Assert.Null(completed.Summary.DeepestRuleReached);
    }

    [Fact]
    public async Task TheDetailsCarryTheParsersOwnSearchStateAndEffort_KeepingUntimedKindsUntimed()
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Completed(TraceEnvelope.Of("-", GoldenTree, guessed: true), string.Empty, TimeSpan.Zero) };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var details = Assert.IsType<PanGlossTraceOutcome.Completed>(outcome).Details!;
        Assert.True(details.SearchCompleted);
        Assert.Equal(17, details.Steps);
        Assert.Equal(287600, details.ElapsedNs);
        Assert.True(details.Guessed);
        var lexEntry = Assert.Single(details.Categories, category => category.Kind == "lexEntry");
        Assert.Equal(4800, lexEntry.SelfElapsedNs);
        var rootIndex = Assert.Single(details.Categories, category => category.Kind == "rootIndex");
        Assert.Equal(1, rootIndex.NoRoot);
        Assert.Null(rootIndex.SelfElapsedNs);
    }

    [Fact]
    public async Task ATraceStoppedAtTheParsersStepCapIsIncomplete_WithItsTreeKept()
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Completed(TraceEnvelope.Of("-", GoldenTree, capped: true), string.Empty, TimeSpan.Zero) };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var incomplete = Assert.IsType<PanGlossTraceOutcome.Incomplete>(outcome);
        Assert.NotNull(incomplete.Tree);
        Assert.False(incomplete.Summary!.Completed);
        Assert.Contains("step cap", incomplete.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATimedOutInvocationIsIncomplete_WithNoTreeAndTheReasonPreserved()
    {
        var stub = new StubInvoker
        {
            Respond = _ => new PanGlossOutcome.TimedOut(
                TimeSpan.FromSeconds(30), "pangloss parse did not finish within 0.5 minutes and was stopped."),
        };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None, TimeSpan.FromSeconds(30));

        var incomplete = Assert.IsType<PanGlossTraceOutcome.Incomplete>(outcome);
        Assert.Equal("sagd", incomplete.Word);
        Assert.Null(incomplete.Tree);
        Assert.Null(incomplete.Summary);
        Assert.Contains("did not finish", incomplete.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADeclinedInvocationCarriesTheParsersOwnMessage()
    {
        var stub = new StubInvoker
        {
            Respond = _ => new PanGlossOutcome.Refused(1, "read golden.xml: not found", string.Empty,
                "pangloss parse exited 1:\nread golden.xml: not found"),
        };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var declined = Assert.IsType<PanGlossTraceOutcome.Declined>(outcome);
        Assert.Contains("not found", declined.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAbsentExecutableMapsToUnavailable()
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Unavailable("Could not find the pangloss executable.") };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var unavailable = Assert.IsType<PanGlossTraceOutcome.Unavailable>(outcome);
        Assert.Contains("Could not find", unavailable.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancellationBeforeAnyWorkStartsIsTheTypedCancellationOutcome_NotAnException()
    {
        var stub = new StubInvoker();
        var tracer = new PanGlossTracer(stub);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", cancelled.Token);

        var typed = Assert.IsType<PanGlossTraceOutcome.Cancelled>(outcome);
        Assert.Equal("sagd", typed.Word);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"pangloss.trace-details.v1\",\"search\":{")]
    [InlineData("{\"schemaVersion\":\"pangloss.trace-details.v2\",\"search\":{},\"result\":{},\"categories\":{},\"trace\":null}")]
    [InlineData("sagd\t32+PAST|sag+?d\n{\"type\":\"WordAnalysis\",\"inputShape\":\"sagd\",\"children\":[]}")]
    [InlineData("no JSON anywhere in this output")]
    public async Task MalformedOrTruncatedOutputIsATypedRefusal_NotAnException(string standardOutput)
    {
        var stub = new StubInvoker { Respond = _ => new PanGlossOutcome.Completed(standardOutput, string.Empty, TimeSpan.Zero) };
        var tracer = new PanGlossTracer(stub);

        var outcome = await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var malformed = Assert.IsType<PanGlossTraceOutcome.Malformed>(outcome);
        Assert.Equal(standardOutput, malformed.RawOutput);
    }

    [Fact]
    public async Task ADefaultTimeoutIsForwardedAsTheWallClockCap_AndIsOverridable()
    {
        var stub = new StubInvoker();
        var tracer = new PanGlossTracer(stub);

        await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);
        Assert.Equal(PanGlossTracer.DefaultTimeout, stub.LastWallClockCap);
        Assert.True(PanGlossTracer.DefaultTimeout < PanGlossInvoker.DefaultWallClockCap);

        await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None, TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), stub.LastWallClockCap);
    }

    [Fact]
    public async Task TheRequestNamesTheGrammarAndTheWord()
    {
        var stub = new StubInvoker();
        var tracer = new PanGlossTracer(stub);

        await tracer.TraceAsync("golden.xml", "sagd", CancellationToken.None);

        var trace = Assert.IsType<PanGlossRequest.Trace>(stub.LastRequest);
        Assert.Equal("golden.xml", trace.GrammarPath);
        Assert.Equal("sagd", trace.Word);
        Assert.Equal("parse", trace.Subcommand);
    }

    /// Records the last request and wall-clock cap; answers with whatever <see cref="Respond"/> says.
    private sealed class StubInvoker : IPanGlossInvoker
    {
        public PanGlossRequest? LastRequest { get; private set; }
        public TimeSpan? LastWallClockCap { get; private set; }

        public Func<PanGlossRequest, PanGlossOutcome> Respond { get; set; } =
            _ => new PanGlossOutcome.Completed(string.Empty, string.Empty, TimeSpan.Zero);

        public Task<PanGlossOutcome> RunAsync(
            PanGlossRequest request, string label, CancellationToken cancellationToken, TimeSpan? wallClockCap = null)
        {
            LastRequest = request;
            LastWallClockCap = wallClockCap;
            if (cancellationToken.IsCancellationRequested)
                return Task.FromResult<PanGlossOutcome>(new PanGlossOutcome.Cancelled());
            return Task.FromResult(Respond(request));
        }
    }
}
