using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Responses;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins <see cref="WordTraceQuery"/> over a canned trace, the same captured <c>sagd</c> derivation
/// <see cref="SIL.Motif.Tests.PanGloss.PanGlossTracerTests"/> pins at the reader level: no Baseline is a
/// typed Refusal, a completed trace maps to successful and failed candidates while preserving missing owner
/// explanations, and a declined parser is a typed Refusal rather than an exception.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class WordTraceQueryTests : IDisposable
{
    /// The same real capture <see cref="SIL.Motif.Tests.PanGloss.PanGlossTracerTests"/> pins at the reader level.
    private static readonly string GoldenStandardOutput = TraceEnvelope.Of("32+PAST|sag+?d",
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
        "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"sagd\",\"children\":[]}]}");

    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent =
        Path.Combine(Path.GetTempPath(), "SIL.Motif.WordTraceQueryTests", Guid.NewGuid().ToString("N"));

    public WordTraceQueryTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void NoBaselineIsATypedRefusal_AndNeverReachesTheParser()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var invoker = new FakeInvoker
        {
            Respond = _ => throw new InvalidOperationException("Must not reach the parser without a Baseline."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("wordtrace.no-baseline", outcome.Refusal!.Code);
        Assert.Contains("motif baseline capture", outcome.Refusal.Message);
        Assert.Empty(invoker.Requests);
    }

    private sealed class RecordingWarmTracer(IPanGlossTracer inner) : IWarmPanGlossTracer
    {
        public List<(string Project, string Token, string Grammar)> WarmCalls { get; } = [];
        public int ColdCalls { get; private set; }

        public Task<PanGlossTraceOutcome> TraceAsync(string grammarPath, string word, CancellationToken cancellationToken,
            TimeSpan? timeout = null)
        {
            ColdCalls++;
            return inner.TraceAsync(grammarPath, word, cancellationToken, timeout);
        }

        public async Task<PanGlossTraceOutcome> TraceAsync(string projectKey, string stateToken, string grammarPath,
            string word, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            WarmCalls.Add((projectKey, stateToken, grammarPath));
            return (await inner.TraceAsync(grammarPath, word, cancellationToken, timeout)) with
                { Steps = 13, WorkSteps = 401 };
        }
    }

    [Fact]
    public void ATraceOfTheBaselineGoesThroughTheWarmSessionKeyedByProjectAndBaselineToken()
    {
        var path = _pristine.CopyProjectFile();
        Capture(path);
        var warm = new RecordingWarmTracer(new PanGlossTracer(new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.Zero),
        }));

        var outcome = WordTraceQuery.Query(new WordTraceRequest(path, "sagd"), warm, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Equal(13, outcome.Value!.ParserSteps);
        Assert.Equal(401, outcome.Value.ParserWorkSteps);
        var call = Assert.Single(warm.WarmCalls);
        Assert.Equal(path, call.Project);
        Assert.Matches("^sha256:[0-9a-f]{64}\\|sha256:[0-9a-f]{64}$", call.Token);
        Assert.Equal(0, warm.ColdCalls);
    }

    [Fact]
    public void TheExplicitParserOverloadUsesTheResidentSessionOnlyWhenWarmIsOn()
    {
        var path = _pristine.CopyProjectFile();
        Capture(path);
        try
        {
            var cold = WordTraceQuery.Query(new WordTraceRequest(path, "motifa"), FakeParser.ExecutablePath,
                CancellationToken.None, null, warm: false);
            Assert.True(cold.Succeeded, cold.Refusal?.Message);
            Assert.Equal(WarmSessionPhase.Idle, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);

            var progress = new List<string?>();
            var warm = WordTraceQuery.Query(new WordTraceRequest(path, "motifa"), FakeParser.ExecutablePath,
                CancellationToken.None, p => progress.Add(p.CurrentWord), warm: true);
            Assert.True(warm.Succeeded, warm.Refusal?.Message);
            Assert.Equal(WarmSessionPhase.Ready, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);
            Assert.Equal(["motifa"], progress);
        }
        finally
        {
            TryWordSessions.Shutdown();
        }
    }

    [Fact]
    public async Task EvictKillsTheProjectsSessionIdempotentlyAndCapturingABaselineEvictsIt()
    {
        var path = _pristine.CopyProjectFile();
        Capture(path);
        try
        {
            Assert.Equal(WarmSessionPhase.Ready, (await TryWordSessions.Prewarm(path, FakeParser.ExecutablePath)).Phase);
            TryWordSessions.Evict(path);
            TryWordSessions.Evict(path);
            Assert.Equal(WarmSessionPhase.Idle, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);

            Assert.Equal(WarmSessionPhase.Ready, (await TryWordSessions.Prewarm(path, FakeParser.ExecutablePath)).Phase);
            Capture(path);
            Assert.Equal(WarmSessionPhase.Idle, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);

            Assert.Equal(WarmSessionPhase.Ready, (await TryWordSessions.Prewarm(path, FakeParser.ExecutablePath)).Phase);
            TryWordSessions.Shutdown();
            Assert.Equal(WarmSessionPhase.Idle, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);
        }
        finally
        {
            TryWordSessions.Shutdown();
        }
    }

    [Fact]
    public async Task PrewarmingAProjectWithoutABaselineIsFailedAndWithOneBecomesReadyThenFiresStateChanged()
    {
        var bare = _pristine.CopyProjectFile();
        var none = await TryWordSessions.Prewarm(bare, FakeParser.ExecutablePath);
        Assert.Equal(WarmSessionPhase.Failed, none.Phase);
        Assert.Equal("wordtrace.no-baseline", none.FailureCode);

        var path = _pristine.CopyProjectFile();
        Capture(path);
        var seen = new System.Collections.Concurrent.ConcurrentQueue<WarmSessionPhase>();
        void Observe(object? sender, WarmSessionStateChangedEventArgs args) => seen.Enqueue(args.State.Phase);
        TryWordSessions.StateChanged += Observe;
        try
        {
            var state = await TryWordSessions.Prewarm(path, FakeParser.ExecutablePath);
            Assert.Equal(WarmSessionPhase.Ready, state.Phase);
            Assert.Equal(WarmSessionPhase.Ready, TryWordSessions.GetState(path, FakeParser.ExecutablePath).Phase);
            await Task.Delay(300);
            Assert.Contains(WarmSessionPhase.Loading, seen);
            Assert.Contains(WarmSessionPhase.Ready, seen);
        }
        finally
        {
            TryWordSessions.StateChanged -= Observe;
            TryWordSessions.Shutdown();
        }
    }

    [Fact]
    public void CapturingAnInvalidShapeRetainsTheParserRefusalWhenReloaded()
    {
        var path = _pristine.CopyProjectFile();
        Capture(path);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(TraceEnvelope.Of("-", null, invalidShape: true),
                string.Empty, TimeSpan.Zero),
        };

        var response = WordTraceQuery.Query(new WordTraceRequest(path, "sagd"),
            new PanGlossTracer(invoker), CancellationToken.None).Value!;

        Assert.True(response.InvalidShape);
        Assert.Equal([ParserRefusals.InvalidShape.Reason], response.Reading.NoParseReasons);
        var reloaded = WordTraceQuery.LoadDiagnostic(response.DiagnosticJson!).Value!;
        Assert.Equal(response.Reading.NoParseReasons, reloaded.Reading.NoParseReasons);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void CapturedLexiconSizeExplainsOnlyAnEmptyBaselineAndSurvivesReload(int count)
    {
        string path;
        using (var cache = _pristine.NewScratch())
        {
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Skip(count).ToArray())
                    entry.Delete();
            });
            new FwDataProjectLoader().Save(cache);
            path = cache.ProjectId.Path;
        }
        var captured = BaselineCaptureCommand.Capture(new(path), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var baseline = captured.Value!;
        using (var live = new FwDataProjectLoader().LoadScratchCache(path))
        {
            NonUndoableUnitOfWorkHelper.Do(live.ActionHandlerAccessor, () =>
                live.ServiceLocator.GetInstance<ILexEntryFactory>().Create(Guid.NewGuid(), live.LangProject.LexDbOA));
            new FwDataProjectLoader().Save(live);
            Assert.Equal(count + 1, live.ServiceLocator.GetInstance<ILexEntryRepository>().Count);
        }
        var json = TraceEnvelope.Of("-", """
            {"type":"WordAnalysis","inputShape":"mtu","children":[
              {"type":"LexicalLookup","source":"Morphology","inputShape":"mtu",
               "lookupResult":{"completed":true,"matchCount":0,"mode":"lexicon","status":"zeroMatches"},
               "children":[]}]}
            """).Replace("\"word\":\"sagd\"", "\"word\":\"mtu\"");
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(json, string.Empty, TimeSpan.Zero),
        };
        var response = WordTraceQuery.Query(new WordTraceRequest(path, "mtu"),
            new PanGlossTracer(invoker), CancellationToken.None).Value!;

        using var rendered = JsonDocument.Parse(ProjectionJson.Serialize(response));
        var host = rendered.RootElement.GetProperty("hostCapture");
        Assert.True(host.TryGetProperty("lexiconEntryCount", out var entries), "The diagnostic must retain its frozen lexicon count.");
        Assert.Equal(count, entries.GetInt32());
        Assert.Equal(baseline.Token, response.HostCapture!.Baseline!.Token);
        Assert.Equal(count == 0, response.Reading.NoParseReasons.Any(reason => reason.Contains("lexicon has 0 entries")));
        Assert.Contains(response.Reading.NoParseReasons, reason => reason.Contains("No lexical root matched 'mtu'"));
        var reloaded = WordTraceQuery.LoadDiagnostic(response.DiagnosticJson!).Value!;
        Assert.Equal(response.Reading.NoParseReasons, reloaded.Reading.NoParseReasons);
        using var saved = JsonDocument.Parse(reloaded.DiagnosticJson!);
        Assert.Equal(count, saved.RootElement.GetProperty("hostCapture").GetProperty("lexiconEntryCount").GetInt32());
    }

    [Fact]
    public void ACompletedTraceMapsSuccessfulAndFailedCandidatesWithoutInventingAnExplanation()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.FromMilliseconds(4)),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.Equal("sagd", response.Word);
        Assert.True(response.Parsed);
        Assert.True(response.Complete);
        Assert.Null(response.StopReason);
        Assert.Equal(13, response.StepCount);
        Assert.Equal(17, response.ParserSteps);
        Assert.Equal(0.2876, response.ParserElapsedMs!.Value, precision: 4);
        Assert.False(response.Guessed);
        // A kind the parser never touched says nothing, so it is left out rather than shown as a row of zeros.
        Assert.Equal(["Lexical entries", "Root lookups"], response.Effort.Select(effort => effort.Kind));
        Assert.Equal(1, response.Effort[1].NoRoot);
        Assert.Null(response.Effort[1].SelfMs);
        Assert.Equal("ed_suffix", response.DeepestRule);
        Assert.Equal("WordAnalysis", response.Reading.Root.Type);
        Assert.Equal(4, response.Reading.Root.Children.Count);

        Assert.Equal(2, response.Reading.Attempts.Count);
        var succeeded = response.Reading.Attempts[0];
        Assert.True(succeeded.Succeeded);
        Assert.Null(succeeded.FailureReason);
        Assert.Null(succeeded.Explanation);
        Assert.Empty(succeeded.Morphs);
        Assert.Equal("Successful", succeeded.Steps[^1].Type);

        var failed = response.Reading.Attempts[1];
        Assert.False(failed.Succeeded);
        Assert.Equal("PartialParse", failed.FailureReason);
        Assert.Null(failed.StoppedByRule);
        Assert.Equal("sag", failed.Surface);
        Assert.Null(failed.Explanation);
        Assert.Equal(TraceEvidenceAvailability.NotRecorded, failed.ExplanationAvailability);
        Assert.Empty(failed.Morphs);
        Assert.Equal("Failed", failed.Steps[^1].Type);
        Assert.Equal(["MorphologicalRuleAnalysis", "Failed"], failed.Steps.TakeLast(2).Select(step => step.Type));
        Assert.Contains(TraceTreeContextRange.Resolve(response.Reading.Root, failed.TreeContext), step => step.FailureReason == "NonPartialRuleProhibitedAfterFinalTemplate");
    }

    [Fact]
    public void WithoutHostEvidenceTheTraceKeepsTheParserAnswerButSkipsTheFieldWorksProjectCopy()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.Zero),
        };

        var full = WordTraceQuery.Query(new WordTraceRequest(fwDataPath, "sagd"),
            new PanGlossTracer(invoker), CancellationToken.None).Value!;
        var light = WordTraceQuery.Query(new WordTraceRequest(fwDataPath, "sagd"),
            new PanGlossTracer(invoker), CancellationToken.None, hostEvidence: false).Value!;

        Assert.NotEmpty(full.HostCapture!.WritingSystems);
        Assert.Empty(light.HostCapture!.WritingSystems);
        Assert.Null(light.HostCapture.LexiconEntryCount);
        Assert.Equal(full.Parsed, light.Parsed);
        Assert.Equal(full.Reading.Attempts.Count, light.Reading.Attempts.Count);
        Assert.Equal(full.Reading.Analyses.Count, light.Reading.Analyses.Count);
        Assert.Equal(full.Reading.NoParseReasons, light.Reading.NoParseReasons);
    }

    [Fact]
    public void LiveTraceRecordsMeasuredHostElapsedTime()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.Zero),
        };
        var tracer = new DelayingTracer(new PanGlossTracer(invoker), TimeSpan.FromMilliseconds(75));

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), tracer, CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.True(outcome.Value!.HostCapture!.WallElapsedMs > 0,
            "A live trace host duration must be measured around the parser invocation.");
    }

    [Fact]
    public void AnOutcomeOnABranchWithNoLookupOfItsOwnDoesNotBorrowANeighbouringBranchsRoot()
    {
        var twoBranches = TraceEnvelope.Of("",
            "{\"type\":\"WordAnalysis\",\"inputShape\":\"xyz\",\"children\":[" +
            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"first\",\"outputShape\":\"xy\",\"children\":[" +
            "{\"type\":\"LexicalLookup\",\"source\":\"S\",\"inputShape\":\"xy\",\"children\":[]}," +
            "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"xy\",\"children\":[]}]}," +
            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"second\",\"outputShape\":\"x\",\"children\":[" +
            "{\"type\":\"Failed\",\"failureReason\":\"PartialParse\",\"outputShape\":\"x\",\"children\":[]}]}]}");
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(twoBranches.Replace("sagd", "xyz", StringComparison.Ordinal), string.Empty, TimeSpan.FromMilliseconds(1)),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "xyz"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var bySource = outcome.Value!.Reading.Attempts.ToDictionary(candidate => candidate.Steps[1].Source!);
        Assert.Empty(bySource["first"].Morphs);
        Assert.Empty(bySource["second"].Morphs);
        Assert.Contains(TraceTreeContextRange.Resolve(outcome.Value!.Reading.Root, bySource["first"].TreeContext), step => step.Type == "LexicalLookup");
        Assert.DoesNotContain(bySource["second"].Steps, step => step.Type == "LexicalLookup");
    }

    [Fact]
    public void AWordWithNoTraceableShapeStillReportsASyntheticRoot()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(TraceEnvelope.Of("-", null, invalidShape: true).Replace("sagd", "zagz", StringComparison.Ordinal), string.Empty, TimeSpan.Zero),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "zagz"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        var response = outcome.Value!;
        Assert.False(response.Parsed);
        Assert.Empty(response.Reading.Attempts);
        Assert.Equal("NoTrace", response.Reading.Root.Type);
        Assert.Empty(response.Reading.Root.Children);
    }

    [Fact]
    public void ATimedOutTraceIsSuccessfulButIncomplete_WithTheStopReasonCarried()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.TimedOut(
                TimeSpan.FromMinutes(2), "pangloss parse did not finish within 2 minutes and was stopped."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.False(outcome.Value!.Complete);
        Assert.Contains("did not finish", outcome.Value.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public void AParserThatIsAbsentIsATypedRefusal_NotAnException()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        Capture(fwDataPath);
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Unavailable("Could not find the pangloss executable."),
        };

        var outcome = WordTraceQuery.Query(
            new WordTraceRequest(fwDataPath, "sagd"), new PanGlossTracer(invoker), CancellationToken.None);

        Assert.False(outcome.Succeeded);
        Assert.Equal("wordtrace.parser-unavailable", outcome.Refusal!.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ARequestUsesTheBaselineSelectedAtExecutionIncludingSameSaveRecapture(bool sameSave)
    {
        var path = _pristine.CopyProjectFile();
        var root = NewManagedRoot();
        var first = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root,
            new FixedClock(DateTimeOffset.Parse("2026-01-01T01:00:00Z"))).Value!;
        var request = new WordTraceRequest(path, "sagd");
        if (!sameSave) File.SetLastWriteTimeUtc(path, first.SourceLastWriteUtc.UtcDateTime.AddMinutes(1));
        var second = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root,
            new FixedClock(DateTimeOffset.Parse("2026-01-01T02:00:00Z"))).Value!;
        if (sameSave)
        {
            Assert.True(second.ReusedExistingBytes);
            Assert.Equal(first.Token, second.Token);
            Assert.Equal(first.SourceLastWriteUtc, second.SourceLastWriteUtc);
        }
        else
        {
            Assert.NotEqual(first.Token, second.Token);
            Assert.NotEqual(first.SourceLastWriteUtc, second.SourceLastWriteUtc);
        }
        var invoker = new FakeInvoker
        {
            Respond = _ => new PanGlossOutcome.Completed(GoldenStandardOutput, string.Empty, TimeSpan.Zero),
        };
        var response = WordTraceQuery.Query(request, new PanGlossTracer(invoker), CancellationToken.None).Value!;
        Assert.Equal(second.FwDataPath, Assert.IsType<PanGlossRequest.Trace>(Assert.Single(invoker.Requests).Request).GrammarPath);
        var selected = response.HostCapture!.Baseline!;
        Assert.Equal(second.Token, selected.Token);
        Assert.Equal(second.SourceLastWriteUtc, selected.SourceLastWriteUtc);
        Assert.Contains(second.Token.CapturedUtc, selected.CaptureDescription);
        var loaded = WordTraceQuery.LoadDiagnostic(response.DiagnosticJson).Value!;
        Assert.Equal(selected, loaded.HostCapture!.Baseline);
    }

    private void Capture(string fwDataPath)
    {
        var captured = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private sealed class DelayingTracer(IPanGlossTracer inner, TimeSpan delay) : IPanGlossTracer
    {
        public async Task<PanGlossTraceOutcome> TraceAsync(
            string grammarPath, string word, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            await Task.Delay(delay, cancellationToken);
            return await inner.TraceAsync(grammarPath, word, cancellationToken, timeout);
        }
    }
}
