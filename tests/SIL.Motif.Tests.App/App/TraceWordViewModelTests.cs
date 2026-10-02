using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="TraceWordViewModel"/>: Try a Word traces only on demand, a chosen Results word primes
/// the box without tracing it, a refusal shows its message, and the candidates and full-derivation views
/// are wrapped for their controls.
/// </summary>
public sealed class TraceWordViewModelTests
{
    [Fact]
    public async Task LiveAndReopenedTracesDisplayTheCapturedRuleName()
    {
        var response = WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!;
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(response);
        var live = new TraceWordViewModel(fake) { WordToTry = "word" };
        live.SetProjectPath(ProjectPath);
        await live.TryCommand.ExecuteAsync(null);
        var reopened = TraceWordViewModel.FromDiagnosticJson(live.DiagnosticJson);

        foreach (var trace in new[] { live, reopened })
        {
            Assert.Equal("Stopped", Assert.Single(trace.ClosestAttempts).StopHeadline);
            Assert.Null(Assert.Single(trace.Reading!.StopGroups).RuleRefId);
            Assert.Equal("Vowel harmony", trace.Root!.Children[0].Source);
            trace.Candidates[0].IsTreeContextExpanded = true;
            Assert.Equal("Vowel harmony", trace.Candidates[0].RecordedTreeContext[0].Source);
            Assert.Equal("Producer name", Assert.Single(trace.Reading!.Refs).Label);
            trace.RuleFilter = "Vowel harmony";
            var filtered = Assert.Single(trace.FilteredRoots).Children[0];
            Assert.Equal("Vowel harmony", filtered.Source);
            Assert.Equal("Producer name", filtered.RecordedStep.Source);
            Assert.Same(trace.Reading.Root.Children[0], filtered.RecordedStep);
            Assert.Same(trace.Reading.Root.Children[0], Assert.Single(trace.RecordedRoots).Children[0].RecordedStep);
        }
    }

    [Fact]
    public void AResponseRoundTripKeepsTheReadingAndItsAttemptSelections()
    {
        var response = MatinluTrace();
        var reading = response.Reading! with
        {
            Refs = response.Reading!.Refs.Select(reference => reference with
            {
                FieldWorks = new TraceFieldWorksTarget("tool", "Tool", "object", "silfw://recorded"),
            }).ToArray(),
        };
        var loaded = ProjectionJson.Deserialize<WordTraceResponse>(ProjectionJson.Serialize(response with { Reading = reading }))!;
        var trace = new TraceWordViewModel { Result = loaded };
        Assert.Equal(ProjectionJson.Serialize(reading), ProjectionJson.Serialize(trace.Reading));
        Assert.NotEmpty(trace.ClosestAttempts);
        Assert.Equal(reading.ClosestAttempts.Take(trace.ClosestAttempts.Count).Select(attempt => attempt.AttemptId),
            trace.ClosestAttempts.Select(attempt => attempt.AttemptId));
    }

    [Fact]
    public void AnUnknownFailureNameIsShownAsACodeWithoutAnExplanation()
    {
        var reason = TraceStepKinds.ExplainReason("FutureUnificationMechanism");
        Assert.Contains("FutureUnificationMechanism", reason);
        Assert.Contains("not recorded", reason);
        Assert.DoesNotContain("future unification mechanism", reason);
    }

    [Fact]
    public void AnIncompleteSearchStaysStoppedEvenWhenItFoundAnAnalysis()
    {
        var trace = new TraceWordViewModel
        {
            Result = new WordTraceResponse("word", true, false, "limit", 1, null, 2, TraceReadingBuilder.Build("word", Leaf("WordAnalysis"), [], [])),
        };

        Assert.Equal("Search incomplete", trace.AnswerText);
        Assert.Equal(Mark.Stopped, trace.AnswerMark);
        Assert.StartsWith("Parsed · ", trace.PageSummaryText);
    }

    [Fact]
    public void PageSummaryNamesTheAnswerAnalysisCountAndParserTime()
    {
        var trace = new TraceWordViewModel { Result = MatinluTrace() };

        Assert.Equal("Parsed · 2 analyses · 0.32 ms", trace.PageSummaryText);
    }

    [Theory]
    [InlineData("attempted", "Tried")]
    [InlineData("successful", "Applied")]
    [InlineData("failed", "Refused")]
    [InlineData("blocked", "Blocked")]
    [InlineData(null, "Outcome not recorded")]
    public void RecordedContextKeepsTheEventOutcomeAndNotation(string? status, string label)
    {
        var record = new TraceStep("MorphologicalRuleSynthesis", "Plural", null, "dogs", null, [])
        {
            StepId = "0.2", SourceIdentityId = "rule-2", OutcomeStatus = status, OutcomeEventType = "rule_event",
        };
        var step = new TraceStepViewModel(record, null);
        var filtered = step.WithChildren([], true);

        Assert.Equal(label, step.RecordedOutcomeText);
        Assert.Contains("MorphologicalRuleSynthesis", step.Notation);
        Assert.Contains("rule_event", step.Notation);
        if (status is not null) Assert.Contains(status, step.Notation);
        Assert.Same(record, filtered.RecordedStep);
        Assert.Equal("0.2", filtered.RecordedStep.StepId);
        Assert.Equal(record.RefId, filtered.RecordedStep.RefId);
        Assert.Null(filtered.Input);
        Assert.Equal("Reason not recorded", filtered.RecordedReasonText);
        Assert.Equal("Rejection details not recorded", filtered.RecordedRejectionText);
    }

    [Fact]
    public void AnUnknownReasonStaysRawAndOnlyCapturedOperandsAppear()
    {
        var step = new TraceStepViewModel(new TraceStep("Failed", null, null, null, "UnknownFutureCode", [])
        {
            FailureRequired = "[debug feature]", FailureEnvironment = "[raw environment]",
        }, null);

        Assert.Equal("UnknownFutureCode", step.RecordedReasonText);
        Assert.Equal("Required: [debug feature]\nEnvironment: [raw environment]", step.RecordedRejectionText);
    }

    [Fact]
    public void ABlockedStepKeepsItsRecordedEventLabel()
    {
        var step = new TraceStepViewModel(new TraceStep("Blocked", "rule", null, null, null, [])
        {
            OutcomeStatus = "blocked",
        }, null);

        Assert.Equal("Blocked", step.StatusText);
        Assert.Equal("Blocked", step.KindText);
        Assert.Equal("Blocked: rule", step.Label);
        Assert.False(step.IsFailure);
    }

    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Theory]
    [InlineData("available")]
    [InlineData("recorded")]
    public void RecordedRejectionEvidenceWithoutOperandsKeepsItsAvailability(string status)
    {
        var record = new TraceStep("Failed", null, null, null, null, [])
        {
            FailureEvidence = new TraceFailureEvidence(null, null, null, status, null, null, null, null, null),
        };
        var step = new TraceStepViewModel(record, null);
        Assert.Equal(TraceEvidenceAvailability.Recorded, record.RejectionDetailsAvailability);
        Assert.Contains("Rejection details recorded", step.RecordedRejectionText);
        Assert.Contains(status, step.RecordedRejectionText);
        Assert.DoesNotContain("Rejection details not recorded", step.RecordedRejectionText);
    }

    [Theory]
    [InlineData(false, "Producer name")]
    [InlineData(true, "Producer name")]
    [InlineData(false, "Vowel harmony")]
    [InlineData(true, "Vowel harmony")]
    public async Task RecordedAndCapturedNamesBothMatchFilters(bool reopened, string name)
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!);
        var trace = new TraceWordViewModel(fake) { WordToTry = "word" };
        trace.SetProjectPath(ProjectPath);
        await trace.TryCommand.ExecuteAsync(null);
        if (reopened) trace = TraceWordViewModel.FromDiagnosticJson(trace.DiagnosticJson);
        var original = trace.Reading!.Root.Children[0];
        trace.RuleFilter = name;
        Assert.Same(original, Assert.Single(Assert.Single(trace.FilteredRoots).Children).RecordedStep);
        trace.RuleFilter = string.Empty;
        trace.SearchText = name;
        Assert.Same(original, Assert.Single(Assert.Single(trace.FilteredRoots).Children).RecordedStep);
    }

    [Fact]
    public void AnUnparsedTraceStillNamesItsResultWithoutMeasurements()
    {
        var response = WordTraceQuery.LoadDiagnostic(TraceEnvelope.CapturedRuleLabel).Value!;
        var trace = new TraceWordViewModel
        {
            Result = response with { Complete = true, ElapsedMs = 0, ParserSteps = null, ParserElapsedMs = null, HostCapture = null },
        };
        Assert.NotEmpty(trace.Candidates);
        Assert.Equal("No parse", trace.PageSummaryText);
    }

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
            DeepestRule: null, ElapsedMs: 1500,
            TraceReadingBuilder.Build("kitabu", Leaf("WordAnalysis"), [], []))
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
        Assert.Contains("No parse", trace.SummaryText, StringComparison.Ordinal);
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
                Surface = surface, StoppedByRule = rule, StoppedByRuleId = rule,
                StoppedByRefId = "morphRule:" + rule, OutcomeStatus = "failed",
            };
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "hawajafika", Parsed: false, Complete: true, StopReason: null, StepCount: 9, DeepestRule: null, ElapsedMs: 5,
            TraceReadingBuilder.Build("hawajafika", Leaf("WordAnalysis"), [
                Attempt("-a", "SurfaceFormMismatch", "hawajafik", 4),
                Attempt("-a", "SurfaceFormMismatch", "hawajaf", 2),
                Attempt("-a", "SurfaceFormMismatch", "haw", 1),
                Attempt("-a", "SurfaceFormMismatch", "ha", 1),
                Attempt("-ja-", "ObligatorySyntacticFeatures", "hawafika", 3),
                new TraceCandidate([], Succeeded: true, null, null, []) { Surface = "other" },
            ], [])));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "hawajafika";

        await trace.TryCommand.ExecuteAsync(null);

        Assert.Equal("No parse", trace.AnswerText);
        Assert.Equal(Mark.NoParse, trace.AnswerMark);
        // The busiest rule leads, and the bar is drawn against it.
        Assert.Equal(["-a", "-ja-"], trace.StopGroups.Select(group => group.RuleText));
        // A word that failed leads with why, so its attempts are never folded away.
        Assert.False(trace.HasDroppedPaths);
        Assert.True(trace.ShowsStopGroups);
        Assert.Equal([4, 1], trace.StopGroups.Select(group => group.Count));
        Assert.Equal(1.0, trace.StopGroups[0].Share);
        Assert.Equal(0.25, trace.StopGroups[1].Share);
        Assert.Contains("5 attempts stopped", trace.StopGroupsSummary, StringComparison.Ordinal);

        // Unfiltered, the closest three of all five: most morphemes first.
        Assert.Equal(["hawajafik", "hawafika", "hawajaf"], trace.ClosestAttempts.Select(attempt => attempt.Surface));
        Assert.Equal("Show the other 2 attempts", trace.MoreAttemptsText);

        trace.SelectStopGroupCommand.Execute(trace.StopGroups[1]);
        Assert.True(trace.StopGroups[1].IsSelected);
        Assert.Equal(["hawafika"], trace.ClosestAttempts.Select(attempt => attempt.Surface));
        Assert.False(trace.HasMoreAttempts);

        trace.SelectStopGroupCommand.Execute(trace.StopGroups[0]);
        Assert.Equal(3, trace.ClosestAttempts.Count);
        Assert.Equal("Show the other attempt stopped by -a", trace.MoreAttemptsText);
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AFailedAttemptUsesOnlyTheRecordedOwnersExplanation(bool captured)
    {
        var candidate = new TraceCandidateViewModel(new TraceCandidate(
            [new ParserReadingMorph("ma-", "PFV", "v", null, false, null), new ParserReadingMorph("tin", "cut", "v", null, false, null)],
            Succeeded: false, "NonPartialRuleProhibitedAfterFinalTemplate", "No rule may apply after the final template.", [])
        {
            Surface = "matin",
            StoppedByRule = "-lu ‘APPL’",
            OutcomeStatus = "failed",
            FailureEvidence = captured ? new TraceFailureEvidence("decisionGate", "rejection-owner",
                "NonPartialRuleProhibitedAfterFinalTemplate", "captured", null,
                "No rule may apply after the final template.", null, null, null) : null,
        });

        Assert.True(candidate.IsFailure);
        Assert.Equal("matin", candidate.Surface);
        Assert.Equal(2, candidate.Morphs.Count);
        Assert.Equal("Stopped by -lu ‘APPL’", candidate.StopHeadline);
        Assert.Equal(captured ? "No rule may apply after the final template."
            : "Explanation not recorded (reason code: NonPartialRuleProhibitedAfterFinalTemplate).", candidate.StopReason);
        Assert.Equal(captured ? "No rule may apply after the final template." : null, candidate.Explanation);
        Assert.Equal("NonPartialRuleProhibitedAfterFinalTemplate", candidate.ParserCode);
        Assert.True(candidate.HasParserCode);
    }

    [Fact]
    public void AParserMorphemeWithNoNameIsOmittedFromTheDisplayedAttempt()
    {
        var candidate = new TraceCandidateViewModel(new TraceCandidate(
            [new ParserReadingMorph("?", "", "", null, false, null), new ParserReadingMorph("tin", "", "", null, false, null)],
            Succeeded: false, "NonPartialRuleProhibitedAfterFinalTemplate", "Further derivation is prohibited.", [])
        {
            OutcomeStatus = "failed",
        });

        var namedMorph = Assert.Single(candidate.Morphs);
        Assert.Equal("tin", namedMorph.Form);
        Assert.Equal("—", namedMorph.GlossOrPlaceholder);
        Assert.DoesNotContain("?", candidate.Gloss);
        Assert.DoesNotContain("unknown morpheme", candidate.Gloss);
    }

    [Theory]
    [InlineData("MorphologicalRuleSynthesis", "Morphological rule")]
    [InlineData("MorphologicalRuleAnalysis", "Morphological rule")]
    [InlineData("MorphologicalRule", "Morphological rule")]
    [InlineData("PhonologicalRuleSynthesis", "Phonological rule")]
    [InlineData("TemplateAnalysisInput", "Affix template")]
    [InlineData("LexicalLookup", "Lexical lookup")]
    [InlineData("StratumSynthesisOutput", "Rule level")]
    [InlineData("CompoundingRuleAnalysis", "Compound rule")]
    [InlineData("SomeFutureStepKind", "Some future step kind")]
    public void AStepsKindReadsInPlainWordsNeverAsTheParsersClassName(string type, string kind)
    {
        Assert.Equal(kind, TraceStepKinds.Describe(type));
        var step = new TraceStepViewModel(new TraceStep(type, "lu", "matinlu", "matin", null, []), deepestStepId: null);
        Assert.Equal($"{kind}: lu", step.Label);
    }

    [Fact]
    public void StepOutcomesReadAsAppliedStoppedOrTried()
    {
        Assert.Equal("applied", new TraceStepViewModel(
            new TraceStep("MorphologicalRule", "lu", "a", "b", null, []) { OutcomeStatus = "succeeded" }, null).StatusText);
        Assert.Equal("stopped", new TraceStepViewModel(
            new TraceStep("MorphologicalRule", "lu", "a", null, "Pattern", []), null).StatusText);
        Assert.Equal("tried", new TraceStepViewModel(
            new TraceStep("MorphologicalRule", "lu", "a", "b", null, []) { OutcomeStatus = "attempted" }, null).StatusText);
    }

    [Fact]
    public void AStopGroupWithoutAnExplanationShowsTheRecordedCode()
    {
        var group = new TraceStopGroupViewModel("lu", "SomeNewReason", null, 2);

        Assert.Equal("Explanation not recorded (reason code: SomeNewReason).", group.ReasonText);
    }

    [Fact]
    public async Task ACandidateKeepsItsIdentityAcrossReads()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: true, Complete: true, StopReason: null, StepCount: 1, DeepestRule: null, ElapsedMs: 1,
            TraceReadingBuilder.Build("kitabu", Leaf("WordSynthesis"), [new TraceCandidate([new ParserReadingMorph("kitabu", "book", "n", null, false, null)], true, null, null, [])], [])));
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
            TraceReadingBuilder.Build("kitabu", Leaf("WordSynthesis", "root"), [new TraceCandidate(
                [new ParserReadingMorph("kitabu", "book", "n", null, false, null)],
                Succeeded: true, FailureReason: null, Explanation: null, Steps: [])], [])));
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
    public void ADeepestEventAddressSelectsOnlyThatOccurrenceOfADuplicateLabel()
    {
        var root = new TraceStep("WordAnalysis", null, null, null, null,
        [
            new TraceStep("MorphologicalRuleAnalysis", "Same", null, null, null, []) { StepId = "0.0" },
            new TraceStep("MorphologicalRuleAnalysis", "Same", null, null, null, []) { StepId = "0.1" },
        ]) { StepId = "0" };
        var trace = new TraceStepViewModel(root, deepestStepId: "0.1");
        Assert.False(trace.Children[0].IsDeepest);
        Assert.True(trace.Children[1].IsDeepest);
    }

    [Fact]
    public void TheDeepestStepIsMarkedByItsSavedTreeAddress()
    {
        var root = new TraceStep("WordSynthesis", "root", "in", "out", null,
            [Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked") with { StepId = "0.0" }]) { StepId = "0" };
        var trace = new TraceStepViewModel(root, deepestStepId: "0.0");

        Assert.False(trace.IsDeepest);
        Assert.True(trace.Children[0].IsDeepest);
    }

    [Fact]
    public void NeutralStepsHaveExplicitStatusWithoutASuccessAlias()
    {
        var neutral = new TraceStepViewModel(Leaf("LexicalLookup", "fik"), deepestStepId: null);
        Assert.False(neutral.IsSuccessful);
        Assert.False(neutral.IsFailure);
        Assert.Equal("tried", neutral.StatusText);
        Assert.Null(typeof(TraceStepViewModel).GetProperty("Passed"));
    }

    [Fact]
    public void ANeutralStepHasNoFailureReasonAndAFailingOneDoes()
    {
        var passing = new TraceStepViewModel(Leaf("LexicalLookup", "fik"), deepestStepId: null);
        var failing = new TraceStepViewModel(Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked"), deepestStepId: null);

        Assert.False(passing.IsSuccessful);
        Assert.Equal("tried", passing.StatusText);
        Assert.False(passing.HasFailureReason);
        Assert.True(failing.IsFailure);
        Assert.True(failing.HasFailureReason);
    }

    [Fact]
    public async Task EachCandidateCarriesItsFullOrderedStepPathNotJustTheFinalFailure()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "hawajafika", Parsed: false, Complete: true, StopReason: null, StepCount: 3, DeepestRule: "neg-ha-",
            ElapsedMs: 12,
            TraceReadingBuilder.Build("hawajafika", Leaf("WordSynthesis", "root"), [new TraceCandidate(
                [new ParserReadingMorph("ha-", "neg", "infl", null, false, null)],
                Succeeded: false, FailureReason: "mismatch", Explanation: "does not match",
                Steps:
                [
                    Leaf("LexicalLookup", "fik"),
                    Leaf("MorphologicalRuleSynthesis", "neg-ha-", "blocked"),
                ])], [])));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "hawajafika";

        await trace.TryCommand.ExecuteAsync(null);

        var candidate = Assert.Single(trace.Candidates);
        Assert.Equal(2, candidate.Steps.Count);
        Assert.False(candidate.Steps[0].IsSuccessful);
        Assert.Equal("tried", candidate.Steps[0].StatusText);
        Assert.True(candidate.Steps[1].IsFailure);
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
    public async Task TheChosenWordsApprovedAnalysisSitsBesideTheAttemptThatGotFurthest()
    {
        var fake = new FakeCommandClient();
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "hawajafika", Parsed: false, Complete: true, StopReason: null, StepCount: 3, DeepestRule: "neg-ha-",
            ElapsedMs: 12,
            TraceReadingBuilder.Build("hawajafika", Leaf("WordSynthesis", "root"), [new TraceCandidate(
                [new ParserReadingMorph("ha-", "neg", "infl", null, false, null)],
                Succeeded: false, FailureReason: "mismatch", Explanation: "does not match", Steps: [])], [])));
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        var approved = new ParserReadingViewModel(1, new ParserReading(
            [new ParserReadingMorph("ha-", "NEG", "", null, false, null), new ParserReadingMorph("fik", "arrive", "v", null, false, null)]),
            "missed");
        trace.SetWord("hawajafika");
        trace.SetExpected("hawajafika", approved.Morphs);

        await trace.TryCommand.ExecuteAsync(null);

        Assert.True(trace.HasComparison);
        Assert.True(trace.ShowsChosenWord);
        Assert.False(trace.ShowsOtherWord);
        Assert.Equal("ha-", Assert.Single(trace.FurthestAttempt!.Morphs).Form);

        // A word typed over the chosen one has no approved analysis to set beside it.
        trace.SetExpected("kitabu", approved.Morphs);
        Assert.False(trace.HasComparison);
        Assert.True(trace.ShowsOtherWord);
    }

    [Fact]
    public async Task TryingAWordTakesTheFullWidth()
    {
        var fake = new FakeCommandClient();
        var trace = new TraceWordViewModel(fake);
        trace.SetProjectPath(ProjectPath);
        trace.WordToTry = "kitabu";
        fake.TraceWordCompletesWith(new WordTraceResponse(
            "kitabu", Parsed: false, Complete: true, StopReason: null, StepCount: 1, DeepestRule: "root", ElapsedMs: 1,
            TraceReadingBuilder.Build("kitabu", Leaf("WordSynthesis", "root"), [], [])));

        await trace.TryCommand.ExecuteAsync(null);

        Assert.True(trace.IsFocused);
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
            TraceReadingBuilder.Build("kitabu", Leaf("WordSynthesis", "root"), [new TraceCandidate(
                [new ParserReadingMorph("kitabu", "book", "n", null, false, null)],
                Succeeded: true, FailureReason: null, Explanation: null, Steps: [])], [])));
        await trace.TryCommand.ExecuteAsync(null);
        trace.SelectedCandidate = trace.Candidates[0];
        trace.SelectedStep = new TraceStepViewModel(Leaf("LexicalLookup", "fik"), deepestStepId: null);

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

    [Theory]
    [InlineData("The parser stopped at its step cap after 1,000,000 steps, so this trace is not the whole search.")]
    [InlineData("The parser stopped at its own time limit, so this trace is not the whole search.")]
    [InlineData("The search was cancelled.")]
    [InlineData(null)]
    [InlineData("")]
    public void AnIncompleteSearchShowsOnlyItsRecordedReason(string? stopReason)
    {
        var response = new WordTraceResponse(
            "kitabu", Parsed: false, Complete: false, StopReason: stopReason, StepCount: 5,
            DeepestRule: null, ElapsedMs: 12,
            TraceReadingBuilder.Build("kitabu", Leaf("WordAnalysis"), [], []))
        {
            SearchStatus = "incomplete",
        };
        var trace = new TraceWordViewModel(new FakeCommandClient()) { Result = response };

        Assert.Equal($"Search incomplete: {(string.IsNullOrWhiteSpace(stopReason) ? "Reason not recorded" : stopReason)}",
            trace.SearchStatusText);
        Assert.DoesNotContain("taking too long", trace.SearchStatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RichResultPutsAnalysesFirstAndKeepsFailedAttemptsCollapsedByDefault()
    {
        var response = new WordTraceResponse(
            "kitabu", Parsed: true, Complete: false, StopReason: "The search reached its limit.", StepCount: 5,
            DeepestRule: null, ElapsedMs: 12,
            TraceReadingBuilder.Build("kitabu", Leaf("WordSynthesis"), [
                new TraceCandidate(
                    [new ParserReadingMorph("ki", "book", "n", "regular", false, "silfw://entry/1")],
                    Succeeded: true, FailureReason: null, Explanation: null, Steps: []),
                new TraceCandidate(
                    [new ParserReadingMorph("ta", "write", "v", null, false, null)],
                    Succeeded: false, FailureReason: "surface-mismatch", Explanation: "The output did not match.", Steps: [])
            ], [new TraceAnalysis("analysis-1", 0, "kitabu", "available",
                [new TraceMorph("morph-1", "ki", "kika", "book", "N", null, null, null, null, null)])]))
        {
            Effort = [new TraceEffort("Lexical entries", 3, 2, 1, 0, 0, 4, 0.5) { Work = 2 }],

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
        Assert.Equal("stopped", trace.Candidates[1].StatusText);
    }
    [Fact]
    public void LoadingProducerEnvelopeKeepsRawJsonAndRecordedRichAnalysis()
    {
        const string json = """
            {"schemaVersion":"pangloss.trace-details.v3","word":"sagd",
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
        Assert.Equal("stopped", trace.Root!.StatusText);
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
            DeepestRule: null, ElapsedMs: 1,
            TraceReadingBuilder.Build("word", new TraceStep(
                "WordSynthesis", "root", "in", "out", null, [child]), [], []));
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
            DeepestRule: null, ElapsedMs: 1,
            TraceReadingBuilder.Build("word", Leaf("Success"), [], [new TraceAnalysis("a", 0, "word", "available",
                [new TraceMorph("m", "form", "head", "gloss", "noun", null, null, null, null, "silfw://entry/1")])]));
        var trace = new TraceWordViewModel { Result = response };

        Assert.False(Assert.Single(Assert.Single(trace.Analyses).Morphs).HasLink);
    }

    [Fact]
    public void AParsedWordLeadsWithItsAnalysesOnceEachAndFoldsThePathsTheParserDropped()
    {
        var trace = new TraceWordViewModel(new FakeCommandClient()) { Result = MatinluTrace() };

        Assert.Equal(2, trace.Analyses.Count);
        Assert.All(trace.Analyses, analysis => Assert.Equal(1, analysis.RecordCount));
        Assert.Equal("Parsed: 2 analyses", trace.AnalysesHeading);

        // The four attempts that stopped are the search's normal tidying up, so they start folded.
        Assert.True(trace.HasDroppedPaths);
        Assert.Equal("4 other paths the parser tried and dropped (normal)", trace.DroppedPathsText);
        Assert.False(trace.ShowsStopGroups);
        Assert.False(trace.ShowsClosestAttempts);

        trace.ShowDroppedPaths = true;
        Assert.True(trace.ShowsStopGroups);
        Assert.True(trace.ShowsClosestAttempts);

        // A new answer folds them again.
        trace.Result = MatinluTrace();
        Assert.False(trace.ShowDroppedPaths);
        Assert.False(trace.ShowsStopGroups);
    }

    [Fact]
    public void AnalysesWithoutIdentityEvidenceRemainSeparateInSourceOrder()
    {
        static TraceAnalysis Analysis(int index, string morphemes) =>
            new($"analysis-{index}", index, "kitabu", "available", []) { LegacyMorphemes = morphemes };
        var trace = new TraceWordViewModel
        {
            Result = new WordTraceResponse(
                "kitabu", Parsed: true, Complete: true, StopReason: null, StepCount: 3, DeepestRule: null, ElapsedMs: 1,
                TraceReadingBuilder.Build("kitabu", Leaf("WordAnalysis"), [new TraceCandidate([], Succeeded: true, null, null, [])], [Analysis(0, "KI+TABU"), Analysis(1, "KITABU"), Analysis(2, "KI+TABU")])),
        };

        Assert.Equal(["Analysis 1", "Analysis 2", "Analysis 3"], trace.Analyses.Select(analysis => analysis.Label));
        Assert.Equal([1, 1, 1], trace.Analyses.Select(analysis => analysis.RecordCount));
        Assert.False(trace.Analyses[1].HasRepeatedRecords);
        Assert.Equal("Parsed: 3 analyses", trace.AnalysesHeading);
        Assert.False(trace.HasDroppedPaths);

        trace.Result = trace.Result! with
        {
            Reading = TraceReadingBuilder.Build(trace.Result.Word, trace.Result.Reading.Root,
                trace.Result.Reading.Attempts, [Analysis(0, "KITABU")]),
        };
        Assert.Equal("Parsed: 1 analysis", trace.AnalysesHeading);
    }

    private static WordTraceResponse MatinluTrace() => WordTraceQuery.LoadDiagnostic(File.ReadAllText(
        Path.Combine(AppContext.BaseDirectory, "TestFixtures", "trace-details-v3-matinlu.json"))).Value!;
}
