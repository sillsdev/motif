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
}
