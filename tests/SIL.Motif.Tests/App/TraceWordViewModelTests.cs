using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="TraceWordViewModel"/>: Try a Word traces only on demand, a chosen Results word primes
/// the box without tracing it, a refusal shows its message, and the candidates and full-derivation views
/// are wrapped for their controls.
/// </summary>
public sealed class TraceWordViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    private static TraceStep Leaf(string type, string? source = null, string? failure = null) =>
        new(type, source, "in", "out", failure, []);

    [Fact]
    public void WithNoProjectTheCommandCannotRun()
    {
        var trace = new TraceWordViewModel(new FakeCommandClient()) { WordToTry = "kitabu" };

        Assert.False(trace.TryCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChoosingAnotherWordCancelsTheTraceStillRunning()
    {
        var fake = new FakeCommandClient();
        CancellationToken seen = default;
        var gate = new TaskCompletionSource<CommandOutcome<WordTraceResponse>>();
        fake.OnTraceWord((_, token) =>
        {
            seen = token;
            token.Register(() => gate.TrySetResult(CommandOutcome<WordTraceResponse>.Refused(
                new Refusal("wordtrace.cancelled", FailureReason.Cancelled, "Cancelled."))));
            return gate.Task;
        });
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";

        var running = trace.TryCommand.ExecuteAsync(null);
        Assert.True(trace.IsLoading);
        Assert.True(trace.CancelCommand.CanExecute(null));
        trace.Reset();
        await running;

        Assert.True(seen.IsCancellationRequested);
        Assert.False(trace.IsLoading);
        Assert.Null(trace.Refusal);
    }

    [Fact]
    public async Task TheSummarySaysHowHardTheParserWorked_AndWhyATraceStoppedShort()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: false, Complete: false, StopReason: "The parser stopped at its step cap.", StepCount: 3,
            DeepestRule: null, ElapsedMs: 1500, [], Leaf("WordAnalysis"))
        {
            ParserSteps = 12345,
            ParserElapsedMs = 0.2876,
            Effort = [new TraceEffort("Root lookups", 2, 0, 0, 1, 0, 0, null)],
        });
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.True(trace.HasResult);
        Assert.Contains("Did not parse", trace.SummaryText, StringComparison.Ordinal);
        Assert.Contains("12,345 parser steps", trace.SummaryText, StringComparison.Ordinal);
        Assert.Contains("0.29 ms in the parser, 1.5 s overall", trace.SummaryText, StringComparison.Ordinal);
        Assert.Equal("The parser stopped at its step cap.", trace.StopReason);
        var root = Assert.Single(trace.Effort);
        Assert.Equal("1 found no root", root.Missed);
        Assert.Equal("not timed", root.Time);

        trace.Reset();
        Assert.False(trace.HasResult);
        Assert.Empty(trace.Effort);
    }

    [Fact]
    public async Task FailedAttemptsAreGroupedByTheRuleThatStoppedThem_ClosestFirstAndFilterable()
    {
        static TraceCandidate Attempt(string rule, string reason, string surface, int morphs) =>
            new([.. Enumerable.Range(0, morphs).Select(index => new ParserReadingMorph($"m{index}", "gloss", "v", null, false, null))],
                Succeeded: false, reason, $"Because of {reason}.", [])
            {
                Surface = surface, StoppedByRule = rule, OutcomeStatus = "failed",
            };
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "hawajafika", Parsed: false, Complete: true, StopReason: null, StepCount: 9, DeepestRule: null, ElapsedMs: 5,
            [
                Attempt("-a", "SurfaceFormMismatch", "hawajafik", 4),
                Attempt("-a", "SurfaceFormMismatch", "hawajaf", 2),
                Attempt("-a", "SurfaceFormMismatch", "haw", 1),
                Attempt("-a", "SurfaceFormMismatch", "ha", 1),
                Attempt("-ja-", "ObligatorySyntacticFeatures", "hawafika", 3),
                new TraceCandidate([], Succeeded: true, null, null, []) { Surface = "other" },
            ],
            Leaf("WordAnalysis")));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "hawajafika";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Equal("Did not parse", trace.AnswerText);
        Assert.Equal(Verdict.NoResult, trace.AnswerVerdict);
        // The busiest rule leads, and the bar is drawn against it.
        Assert.Equal(["-a", "-ja-"], trace.StopGroups.Select(group => group.RuleText));
        Assert.Equal([4, 1], trace.StopGroups.Select(group => group.Count));
        Assert.Equal(1.0, trace.StopGroups[0].Share);
        Assert.Equal(0.25, trace.StopGroups[1].Share);
        Assert.Contains("2 rules stopped all 5 attempts", trace.StopGroupsSummary, StringComparison.Ordinal);

        // Unfiltered, the closest three of all five: most morphemes first.
        Assert.Equal(["hawajafik", "hawafika", "hawajaf"], trace.ClosestAttempts.Select(attempt => attempt.Surface));
        Assert.Equal("Show the other 2 attempts", trace.MoreAttemptsText);

        trace.SelectStopGroupCommand.Execute(trace.StopGroups[1]);
        Assert.True(trace.StopGroups[1].IsSelected);
        Assert.Equal(["hawafika"], trace.ClosestAttempts.Select(attempt => attempt.Surface));
        Assert.False(trace.HasMoreAttempts);

        trace.SelectStopGroupCommand.Execute(trace.StopGroups[0]);
        Assert.Equal(3, trace.ClosestAttempts.Count);
        Assert.Equal("Show the other 1 stopped by -a", trace.MoreAttemptsText);
        trace.ShowEveryAttemptCommand.Execute(null);
        Assert.Equal(4, trace.ClosestAttempts.Count);

        // Choosing the group in force again clears the filter.
        trace.SelectStopGroupCommand.Execute(trace.StopGroups[0]);
        Assert.Null(trace.SelectedStopGroup);
        Assert.Equal(3, trace.ClosestAttempts.Count);
    }

    [Fact]
    public void TheEffortTableShadesEachNumberAgainstTheLargestInItsOwnColumn()
    {
        var rows = TraceEffortViewModel.Table(
        [
            new TraceEffort("Affix and derivation rules", 12, 8, 4, 0, 0, 4, 0.052) { Work = 64 },
            new TraceEffort("Root lookups", 5, 0, 0, 1, 0, 0, null) { Work = 33 },
        ]);

        Assert.Equal(1.0, TraceEffortViewModel.Intensity(12, 12));
        Assert.Equal(0.0, TraceEffortViewModel.Intensity(0, 12));
        Assert.Equal(0.7, rows[0].TriedHeat, precision: 6);
        Assert.True(rows[1].TriedHeat is > 0 and < 0.7);
        Assert.Equal(0, rows[1].ProducedHeat);
        Assert.Equal(0, rows[1].TimeHeat);
    }

    [Fact]
    public void AFailedAttemptSaysWhichRuleStoppedItAndWhy()
    {
        var candidate = new TraceCandidateViewModel(new TraceCandidate(
            [new ParserReadingMorph("ma-", "PFV", "v", null, false, null), new ParserReadingMorph("tin", "cut", "v", null, false, null)],
            Succeeded: false, "NonPartialRuleProhibitedAfterFinalTemplate", "No rule may apply after the final template.", [])
        {
            Surface = "matin",
            StoppedByRule = "-lu ‘APPL’",
            OutcomeStatus = "failed",
        });

        Assert.True(candidate.IsFailure);
        Assert.Equal("matin", candidate.Surface);
        Assert.Equal(2, candidate.Morphs.Count);
        Assert.Equal("Stopped by -lu ‘APPL’", candidate.StopHeadline);
        Assert.Equal("No rule may apply after the final template. (NonPartialRuleProhibitedAfterFinalTemplate)", candidate.StopReason);
    }

    [Fact]
    public async Task ACandidateKeepsItsIdentityAcrossReads()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: true, Complete: true, StopReason: null, StepCount: 1, DeepestRule: null, ElapsedMs: 1,
            [new TraceCandidate([new ParserReadingMorph("kitabu", "book", "n", null, false, null)], true, null, null, [])],
            Leaf("WordSynthesis")));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Same(trace.Candidates[0], trace.Candidates[0]);
    }

    [Fact]
    public void SetWordFillsTheBoxWithoutStartingATrace()
    {
        var fake = new FakeCommandClient();
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);

        trace.SetWord("kitabu");

        Assert.Equal("kitabu", trace.WordToTry);
        Assert.Empty(fake.TraceWordRequests);
        Assert.Null(trace.Result);
    }

    [Fact]
    public async Task TryingAWordCallsTheClientWithTheTypedWordAndPublishesCandidates()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: true, Complete: true, StopReason: null, StepCount: 3, DeepestRule: "root",
            ElapsedMs: 12,
            [new TraceCandidate(
                [new ParserReadingMorph("kitabu", "book", "n", null, false, null)],
                Succeeded: true, FailureReason: null, Explanation: null, Steps: [])],
            Leaf("WordSynthesis", "root")));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Equal("kitabu", Assert.Single(fake.TraceWordRequests).Word);
        var candidate = Assert.Single(trace.Candidates);
        Assert.True(candidate.Succeeded);
        Assert.Equal("kitabu", candidate.Text);
        Assert.NotNull(trace.Root);
        Assert.Single(trace.Roots);
    }

    [Fact]
    public async Task ARefusalShowsItsMessageAndLeavesNoResult()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("app.not-built", FailureReason.Refused, "The word trace query is not built yet.");
        fake.OnTraceWord((_, _) => Task.FromResult(CommandOutcome<WordTraceResponse>.Refused(refusal)));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Equal(refusal.Message, trace.Refusal!.Message);
        Assert.Null(trace.Result);
        Assert.Empty(trace.Candidates);
    }

    [Fact]
    public void TheDeepestStepIsMarkedFromTheResponsesDeepestRule()
    {
        var root = new TraceStep("WordSynthesis", "root", "in", "out", null,
            [Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked")]);
        var trace = new TraceStepViewModel(root, deepestRule: "neg-ha-");

        Assert.False(trace.IsDeepest);
        Assert.True(trace.Children[0].IsDeepest);
    }

    [Fact]
    public void APassingStepHasNoFailureReasonAndAFailingOneDoes()
    {
        var passing = new TraceStepViewModel(Leaf("LexicalLookup", "fik"), deepestRule: null);
        var failing = new TraceStepViewModel(Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked"), deepestRule: null);

        Assert.True(passing.Passed);
        Assert.False(passing.HasFailureReason);
        Assert.False(failing.Passed);
        Assert.True(failing.HasFailureReason);
    }

    [Fact]
    public async Task EachCandidateCarriesItsFullOrderedStepPathNotJustTheFinalFailure()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "hawajafika", Parsed: false, Complete: true, StopReason: null, StepCount: 3, DeepestRule: "neg-ha-",
            ElapsedMs: 12,
            [new TraceCandidate(
                [new ParserReadingMorph("ha-", "neg", "infl", null, false, null)],
                Succeeded: false, FailureReason: "mismatch", Explanation: "does not match",
                Steps:
                [
                    Leaf("LexicalLookup", "fik"),
                    Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked"),
                ])],
            Leaf("WordSynthesis", "root")));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "hawajafika";

        await trace.TryCommand.ExecuteAsync(null);

        var candidate = Assert.Single(trace.Candidates);
        Assert.Equal(2, candidate.Steps.Count);
        Assert.True(candidate.Steps[0].Passed);
        Assert.False(candidate.Steps[1].Passed);
        Assert.Equal("blocked", candidate.Steps[1].FailureReason);
    }

    [Fact]
    public void SelectingACandidateOrAStepIsIndependentStateAThirdPartyViewCanReadLater()
    {
        var trace = new TraceWordViewModel(new FakeCommandClient());
        var candidate = new TraceCandidateViewModel(new TraceCandidate(
            [new ParserReadingMorph("fik", "arrive", "v", null, false, null)],
            Succeeded: true, FailureReason: null, Explanation: null, Steps: [Leaf("LexicalLookup", "fik")]));

        trace.SelectedCandidate = candidate;
        trace.SelectedStep = candidate.Steps[0];

        Assert.Same(candidate, trace.SelectedCandidate);
        Assert.Same(candidate.Steps[0], trace.SelectedStep);
    }

    [Fact]
    public void FocusingTryAWordTogglesTheLabelThatNamesTheNextMove()
    {
        var trace = new TraceWordViewModel(new FakeCommandClient());
        Assert.Equal("Focus this word", trace.FocusToggleLabel);

        trace.IsFocused = true;

        Assert.Equal("Show Analyses", trace.FocusToggleLabel);
    }

    [Fact]
    public async Task TryingAWordClearsTheSelectedCandidateAndStepFromTheOldResult()
    {
        var fake = new FakeCommandClient();
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: true, Complete: true, StopReason: null, StepCount: 1, DeepestRule: "root", ElapsedMs: 1,
            [new TraceCandidate(
                [new ParserReadingMorph("kitabu", "book", "n", null, false, null)],
                Succeeded: true, FailureReason: null, Explanation: null, Steps: [])],
            Leaf("WordSynthesis", "root")));
        await trace.TryCommand.ExecuteAsync(null);
        trace.SelectedCandidate = trace.Candidates[0];
        trace.SelectedStep = new TraceStepViewModel(Leaf("LexicalLookup", "fik"), deepestRule: null);

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Null(trace.SelectedCandidate);
        Assert.Null(trace.SelectedStep);
    }

    [Fact]
    public void ResetClearsTheResultAndReturnsToTheCandidatesView()
    {
        var trace = new TraceWordViewModel(new FakeCommandClient());
        trace.SetViewCommand.Execute(TraceView.FullDerivation);

        trace.Reset();

        Assert.Equal(TraceView.Candidates, trace.View);
        Assert.Null(trace.Result);
        Assert.Empty(trace.Roots);
    }

    [Fact]
    public void RichResultPutsAnalysesFirstAndKeepsFailedAttemptsCollapsedByDefault()
    {
        var response = new WordTraceResponse(
            "kitabu", Parsed: true, Complete: false, StopReason: "The search reached its limit.", StepCount: 5,
            DeepestRule: null, ElapsedMs: 12,
            [
                new TraceCandidate(
                    [new ParserReadingMorph("ki", "book", "n", "regular", false, "silfw://entry/1")],
                    Succeeded: true, FailureReason: null, Explanation: null, Steps: []),
                new TraceCandidate(
                    [new ParserReadingMorph("ta", "write", "v", null, false, null)],
                    Succeeded: false, FailureReason: "surface-mismatch", Explanation: "The output did not match.", Steps: [])
            ],
            Leaf("WordSynthesis"))
        {
            Effort = [new TraceEffort("Lexical entries", 3, 2, 1, 0, 0, 4, 0.5) { Work = 2 }],
            Analyses = [new TraceAnalysis("analysis-1", 0, "kitabu", "available",
                [new TraceMorph("morph-1", "ki", "kika", "book", "N", null, null, null, null, null)])],
        };
        var trace = new TraceWordViewModel(new FakeCommandClient()) { Result = response };

        Assert.Single(trace.Analyses);
        Assert.Equal("ki", trace.Analyses[0].Morphs[0].Form);
        Assert.Equal("kika", trace.Analyses[0].Morphs[0].Headword);
        Assert.Equal("book", trace.Analyses[0].Morphs[0].Gloss);
        Assert.Equal("N", trace.Analyses[0].Morphs[0].Category);
        Assert.Equal(1, trace.FailedAttemptCount);
        Assert.Equal("Search incomplete: The search reached its limit.", trace.SearchStatusText);
        Assert.Equal("4", trace.Effort[0].Uses);
        Assert.Equal("2", trace.Effort[0].Work);
        Assert.Equal("failed", trace.Candidates[1].StatusText);
    }
    [Fact]
    public void LoadingProducerEnvelopeKeepsRawJsonAndRecordedRichAnalysis()
    {
        const string json = """
            {"schemaVersion":"pangloss.trace-details.v2","word":"sagd",
             "search":{"completed":true,"capped":false,"timedOut":false,"invalidShape":false,"steps":1,"elapsedNs":9},
             "result":{"signature":"a","guessed":false,"analyses":[
               {"analysisId":"analysis-0","index":0,"surface":"sagd","morphemes":"root",
                "projection":{"profile":"fieldworks-parse-analysis/v1","status":"available"},
                "morphs":[{"identity":{"formId":"f1","entryId":"e1","msaId":"m1","quality":"exact"},
                  "form":{"text":"sag","writingSystem":"en","sourceId":"f1"},
                  "headword":{"text":"say"},"gloss":{"text":"say"},
                  "msa":{"category":{"name":"verb","abbreviation":"v"}}}] }]},
             "categories":{},
             "trace":{"type":"Failed","children":[],"outcome":{"status":"failed","eventType":"surface-mismatch"},
                      "failureContext":{"reason":"required feature missing"}},
             "hostCapture":{"projectIdentity":"project-a","grammarHash":"abc","grammarHashSemantics":"snapshot-semantic-sha256-v1",
                             "capturedUtc":"2026-09-22T12:00:00Z","wallElapsedMs":1234,
                             "writingSystems":[{"id":"ar","name":"Arabic","isVernacular":true,"isDefault":true,"direction":"rtl","font":"Noto Sans Arabic"}]}}
            """;

        var trace = TraceWordViewModel.FromDiagnosticJson(json);

        Assert.Equal(json, trace.DiagnosticJson);
        var analysis = Assert.Single(trace.Analyses);
        Assert.Equal("say", Assert.Single(analysis.Morphs).Headword);
        Assert.Equal("root", analysis.LegacyMorphemes);
        Assert.Equal(1234, trace.Result!.HostCapture!.WallElapsedMs);
        Assert.Contains("ar", trace.WritingSystemSummary, StringComparison.Ordinal);
        Assert.Contains("project-a", trace.CaptureDetails, StringComparison.Ordinal);
        Assert.Equal("failed", trace.Root!.StatusText);
    }

    [Fact]
    public void InvalidSavedDiagnosticIsRejectedWithoutAProject()
    {
        var exception = Assert.Throws<JsonException>(() => TraceWordViewModel.FromDiagnosticJson(
            "{\"schemaVersion\":\"unsupported.major\",\"word\":\"word\"}"));

        Assert.Contains("schema", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void MorphSearchKeepsMatchingDescendantAndAncestorAndExpandsThePath()
    {
        var child = new TraceStep("Failed", "rule-x", "in", "out", "blocked", [])
        {
            OutcomeStatus = "failed",
            AttemptedMorphs = [new TraceMorph("m1", "needle", "head", "gloss", "verb", null, null, null, null, null)],
        };
        var response = new WordTraceResponse(
            "word", Parsed: false, Complete: true, StopReason: null, StepCount: 2,
            DeepestRule: null, ElapsedMs: 1, Candidates: [], Root: new TraceStep(
                "WordSynthesis", "root", "in", "out", null, [child]));
        var trace = new TraceWordViewModel { Result = response };

        trace.MorphFilter = "needle";

        var root = Assert.Single(trace.FilteredRoots);
        Assert.True(root.ExpandForFilter);
        Assert.Single(root.Children);
        Assert.Equal("rule-x", root.Children[0].Source);
        Assert.Equal(0, trace.HiddenStepCount);
    }

    [Fact]
    public void StandaloneRichMorphDoesNotEnableAFileProvidedFieldWorksLink()
    {
        var response = new WordTraceResponse(
            "word", Parsed: true, Complete: true, StopReason: null, StepCount: 1,
            DeepestRule: null, ElapsedMs: 1, Candidates: [], Root: Leaf("Success"))
        {
            Analyses = [new TraceAnalysis("a", 0, "word", "available",
                [new TraceMorph("m", "form", "head", "gloss", "noun", null, null, null, null, "silfw://entry/1")])],
        };
        var trace = new TraceWordViewModel { Result = response };

        Assert.False(Assert.Single(Assert.Single(trace.Analyses).Morphs).HasLink);
    }
}
