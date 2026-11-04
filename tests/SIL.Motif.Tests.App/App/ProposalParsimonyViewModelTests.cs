using System.Text.RegularExpressions;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins how the Proposal inspector words the automatic Parsimony status, and that watching it only ever reads.
/// </summary>
public sealed class ProposalParsimonyViewModelTests
{
    private const string Project = @"C:\projects\one.fwdata";
    private const string Id = "proposal/aaaaaaaaaaaaaaaaaaaaaa";

    public static TheoryData<ProposalParsimonyState, string, string> SevenStates => new()
    {
        { ProposalParsimonyState.NotSetUp, "Not Set Up", "until PanGloss is set up" },
        { ProposalParsimonyState.NotYetRun, "Not Yet Run", "No Trial has finished yet" },
        { ProposalParsimonyState.Queued, "Queued", "waiting for other work" },
        { ProposalParsimonyState.Running, "Running", "is measuring this Proposal's candidate" },
        { ProposalParsimonyState.Ready, "Ready", "advice is ready" },
        { ProposalParsimonyState.Stale, "Out of Date", "measured before something changed" },
        { ProposalParsimonyState.Failed, "Did Not Finish", "did not finish" },
    };

    [Theory]
    [MemberData(nameof(SevenStates))]
    public async Task EachOfTheSevenStatesReadsAsItself(ProposalParsimonyState state, string label, string phrase)
    {
        var status = StatusFor(state);
        var model = await WatchedAsync(status);

        Assert.True(model.IsVisible);
        Assert.Equal(label, model.StateLabel);
        Assert.Contains(phrase, model.Sentence);
    }

    [Fact]
    public void NoTwoStatesShareALabel()
    {
        var labels = Enum.GetValues<ProposalParsimonyState>().Select(state =>
            ProposalParsimonyText.StateLabel(state)).ToArray();

        Assert.Equal(7, labels.Distinct().Count());
    }

    [Fact]
    public async Task ReadyDoesNotClaimToBeCurrentOnJudgmentsOrTheSelection()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id));

        Assert.True(model.HasNote);
        Assert.Contains("human judgment", model.Note);
        Assert.Contains("Parsimony Selection", model.Note);
        Assert.Contains("may be behind", model.Note);
        Assert.DoesNotMatch(new Regex("up to date|is current|fully checked", RegexOptions.IgnoreCase), AllText(model));
    }

    [Fact]
    public async Task StaleNamesEveryInputThatChangedIncludingTheReservedOnes()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Stale(Id,
            ProposalParsimonyStaleReason.BaselineChanged, ProposalParsimonyStaleReason.ParserChanged,
            ProposalParsimonyStaleReason.SelectionChanged, ProposalParsimonyStaleReason.JudgmentsChanged));

        Assert.Equal("This advice was measured before something changed: the Baseline, PanGloss, " +
            "the Parsimony Selection, a human judgment. It may no longer be right.", model.Sentence);
    }

    [Fact]
    public async Task StaleWithNoNamedReasonSaysItCouldNotBeChecked()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Stale(Id));

        Assert.Equal("Whether this advice is still current could not be checked.", model.Sentence);
    }

    [Theory]
    [InlineData(ProposalParsimonyFailureCategory.TimedOut, "PanGloss ran past its time limit")]
    [InlineData(ProposalParsimonyFailureCategory.ParserUnavailable, "PanGloss was missing")]
    [InlineData(ProposalParsimonyFailureCategory.FactsFailed, "PanGloss could not read the grammar")]
    [InlineData(ProposalParsimonyFailureCategory.EvidenceFailed, "could not build the evidence")]
    [InlineData(ProposalParsimonyFailureCategory.Cancelled, "The run was cancelled")]
    [InlineData(ProposalParsimonyFailureCategory.Internal, "Something unexpected stopped the run")]
    public async Task AFailedJobReadsAsAdviceThatDidNotFinishNeverAsABlocker(
        ProposalParsimonyFailureCategory category, string cause)
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Failed(Id, category));

        Assert.Equal("Did Not Finish", model.StateLabel);
        Assert.StartsWith("Parsimony advice did not finish.", model.Sentence);
        Assert.Contains(cause, model.Sentence);
        Assert.EndsWith("The Trial, Readiness and Apply are not affected.", model.Sentence);
        Assert.DoesNotMatch(new Regex("block|error|cannot apply|must", RegexOptions.IgnoreCase), AllText(model));
    }

    [Fact]
    public async Task ATimedOutGrammarThatIsNotQueuedAgainSaysSo()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Failed(Id,
            ProposalParsimonyFailureCategory.TimedOut, code: "status-skipped-timeout"));

        Assert.Contains("not asked for again by itself", model.Sentence);
    }

    [Fact]
    public async Task EachGroupShowsItsOwnStateWithPerMeasureCountsAndNoScore()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id));

        Assert.Equal(["Grammar Group", "Text Group"], model.Groups.Select(group => group.Title));
        Assert.Equal(["Ready", "Not Set Up"], model.Groups.Select(group => group.StateLabel));
        Assert.Equal(
        [
            "Repeated ad hoc restrictions: 2 new, 1 resolved, 5 unchanged, 3 with changed evidence",
            "Environment may be too broad: 0 new, 0 resolved, 8 unchanged, 0 with changed evidence",
            "Unused grammar statements: not available",
        ], model.Groups[0].Measures);
        Assert.Empty(model.Groups[1].Measures);
        Assert.DoesNotMatch(new Regex("score|total|overall|grade|rating", RegexOptions.IgnoreCase), AllText(model));
    }

    [Fact]
    public async Task TheTextGroupIsNotAutomaticAndSaysWhatItNeeds()
    {
        var needsSelection = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id));
        Assert.Equal("The Text Group does not run on its own, and it needs a Parsimony Selection first.",
            needsSelection.Groups[1].Note);

        var status = ProposalParsimonyStatusFixtures.Ready(Id) with
        {
            Groups =
            [
                ProposalParsimonyStatusFixtures.Measured(),
                ProposalParsimonyStatusFixtures.TextGroup(ProposalParsimonyGroupState.NotYetRun),
            ],
        };
        var notYetRun = await WatchedAsync(status);
        Assert.Equal("Not Yet Run", notYetRun.Groups[1].StateLabel);
        Assert.Equal("The Text Group does not run on its own.", notYetRun.Groups[1].Note);
    }

    [Theory]
    [InlineData(ProposalParsimonyGroupState.Abstained, "Could Not Judge", "This is not a clean result")]
    [InlineData(ProposalParsimonyGroupState.Failed, "Did Not Finish", "failed while reading the evidence")]
    public async Task AGroupThatJudgedNothingNeverReadsAsClean(ProposalParsimonyGroupState state, string label,
        string phrase)
    {
        var grammar = ProposalParsimonyStatusFixtures.Measured() with { State = state, Changes = [] };
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Ready(Id).With(grammar));

        Assert.Equal(label, model.Groups[0].StateLabel);
        Assert.Contains(phrase, model.Groups[0].Note);
    }

    [Fact]
    public async Task ARunningJobNamesItsStepAndShowsNoTimeOrDuration()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Running(Id, "measure", 3, 6));

        Assert.Equal("Parsimony is measuring this Proposal's candidate: measuring, stage 3 of 6.", model.Sentence);
        Assert.DoesNotMatch(new Regex(@"\d\d:\d\d|\b\d{4}-\d\d|\bms\b|second|minute|ago|elapsed", RegexOptions.IgnoreCase),
            AllText(model));
    }

    [Fact]
    public async Task AQueuedJobNamesWhatItIsWaitingBehindWithoutChangingTheShape()
    {
        var other = await WatchedAsync(ProposalParsimonyStatusFixtures.Waiting(Id));
        var behindTrial = await WatchedAsync(ProposalParsimonyStatusFixtures.Waiting(Id, "trial"));
        var running = await WatchedAsync(ProposalParsimonyStatusFixtures.Running(Id));

        Assert.Contains("a Trial", behindTrial.Sentence);
        Assert.Equal(other.Groups.Count, running.Groups.Count);
        Assert.Equal(other.HasNote, running.HasNote);
        Assert.Equal(other.Groups.Select(group => group.Measures.Count), running.Groups.Select(group => group.Measures.Count));
    }

    [Fact]
    public async Task ARefusedReadShowsASentenceAndTheRefusalAndStopsReading()
    {
        var reads = 0;
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Refused(
                new Refusal("store.unreadable", FailureReason.Refused, "Unreadable.")));
        }, NeverDelay);

        await model.Watch(Project, Id);

        Assert.True(model.IsVisible);
        Assert.NotNull(model.ShownRefusal);
        Assert.Equal("Parsimony status could not be read.", model.Sentence);
        Assert.Equal(1, reads);
    }

    [Fact]
    public async Task WatchingUpdatesWhileTheJobRunsAndReadsNothingElse()
    {
        var fake = new FakeCommandClient();
        var script = new Queue<ProposalParsimonyStatus>(
        [
            ProposalParsimonyStatusFixtures.NotYetRun(Id, "status-not-queued"),
            ProposalParsimonyStatusFixtures.Waiting(Id),
            ProposalParsimonyStatusFixtures.Running(Id, "facts", 1, 5),
            ProposalParsimonyStatusFixtures.Running(Id, "measure", 3, 5),
            ProposalParsimonyStatusFixtures.Ready(Id),
        ]);
        fake.OnProposalParsimonyStatus((_, _) => Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(script.Dequeue())));
        var delays = new ManualDelays();
        var model = new ProposalParsimonyViewModel(fake.ReadProposalParsimonyStatusAsync, delays.Delay);

        var watching = model.Watch(Project, Id);
        var seen = new List<string> { model.StateLabel };
        for (var step = 0; step < 4; step++)
        {
            await delays.ReleaseNextAsync(watching);
            seen.Add(model.StateLabel + "|" + model.Sentence);
        }
        await watching;

        Assert.Equal(["Not Yet Run", "Queued|Parsimony is waiting for other work to finish before it starts.",
            "Running|Parsimony is measuring this Proposal's candidate: reading the grammar, stage 1 of 5.",
            "Running|Parsimony is measuring this Proposal's candidate: measuring, stage 3 of 5.",
            "Ready|" + model.Sentence], seen);
        Assert.Equal(5, fake.ProposalParsimonyStatusRequests.Count);
        Assert.All(fake.ProposalParsimonyStatusRequests, request =>
        {
            Assert.Equal(Project, request.FwDataPath);
            Assert.Equal(Id, request.ProposalId);
        });
        Assert.Empty(fake.EnqueueParsimonyRequests);
        Assert.Empty(fake.EnqueueTrialRequests);
        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.WaitForJobRequests);
        Assert.Empty(fake.CaptureBaselineRequests);
        Assert.All(delays.Waits, wait => Assert.Equal(ProposalParsimonyViewModel.PollInterval, wait));
    }

    [Theory]
    [InlineData(ProposalParsimonyState.NotSetUp)]
    [InlineData(ProposalParsimonyState.Ready)]
    [InlineData(ProposalParsimonyState.Stale)]
    [InlineData(ProposalParsimonyState.Failed)]
    public async Task AFinalStateIsReadOnceAndNeverPolled(ProposalParsimonyState state)
    {
        var reads = 0;
        var delays = new ManualDelays();
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(StatusFor(state)));
        }, delays.Delay);

        await model.Watch(Project, Id);

        Assert.Equal(1, reads);
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task ATrialThatHasNotFinishedIsReadOnceBecauseNothingCanQueueYet()
    {
        var reads = 0;
        var delays = new ManualDelays();
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.NotYetRun(Id, "status-no-trial")));
        }, delays.Delay);

        await model.Watch(Project, Id);

        Assert.Equal(1, reads);
        Assert.Empty(delays.Waits);
    }

    [Fact]
    public async Task AFinishedTrialWithNoJobYetIsWatchedForAFewReadsOnly()
    {
        var reads = 0;
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.NotYetRun(Id, "status-not-queued")));
        }, ImmediateDelay);

        await model.Watch(Project, Id);

        Assert.Equal(ProposalParsimonyViewModel.NotQueuedGraceReads + 1, reads);
        Assert.Equal("Not Yet Run", model.StateLabel);
    }

    [Fact]
    public async Task APollSlowsDownAfterTheFastReadsButNeverGivesUpBeforeTheJobFinishes()
    {
        var reads = 0;
        var waits = new List<TimeSpan>();
        var total = ProposalParsimonyViewModel.FastPolls + 20;
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(reads <= total
                ? ProposalParsimonyStatusFixtures.Running(Id) : ProposalParsimonyStatusFixtures.Ready(Id)));
        }, (span, _) =>
        {
            waits.Add(span);
            return Task.CompletedTask;
        });

        await model.Watch(Project, Id);

        Assert.Equal(total + 1, reads);
        Assert.Equal("Ready", model.StateLabel);
        Assert.Equal(ProposalParsimonyViewModel.FastPolls, waits.Count(span => span == ProposalParsimonyViewModel.PollInterval));
        Assert.Equal(20, waits.Count(span => span == ProposalParsimonyViewModel.SlowPollInterval));
        Assert.Equal(ProposalParsimonyViewModel.SlowPollInterval, waits[^1]);
    }

    [Fact]
    public async Task ABusyProjectNeverEndsTheWatchAndTheLastGoodStateStaysShown()
    {
        var reads = 0;
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(reads switch
            {
                1 => CommandOutcome<ProposalParsimonyStatus>.Success(ProposalParsimonyStatusFixtures.Running(Id)),
                2 or 3 => CommandOutcome<ProposalParsimonyStatus>.Refused(
                    new Refusal("project.busy", FailureReason.Busy, "Busy.")),
                _ => CommandOutcome<ProposalParsimonyStatus>.Success(ProposalParsimonyStatusFixtures.Ready(Id)),
            });
        }, ImmediateDelay);
        var shownWhileBusy = new List<string>();
        model.PropertyChanged += (_, _) => { if (reads is 2 or 3) shownWhileBusy.Add(model.StateLabel); };

        await model.Watch(Project, Id);

        Assert.Equal(4, reads);
        Assert.Equal("Ready", model.StateLabel);
        Assert.Null(model.ShownRefusal);
        Assert.All(shownWhileBusy, label => Assert.Equal("Running", label));
    }

    [Fact]
    public async Task ABusyFirstReadKeepsReadingAndThenShowsTheStatus()
    {
        var reads = 0;
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(reads == 1
                ? CommandOutcome<ProposalParsimonyStatus>.Refused(new Refusal("project.busy", FailureReason.Busy, "Busy."))
                : CommandOutcome<ProposalParsimonyStatus>.Success(ProposalParsimonyStatusFixtures.Ready(Id)));
        }, ImmediateDelay);
        var reading = new List<bool>();
        model.PropertyChanged += (_, _) => { if (reads == 1) reading.Add(model.IsLoading); };

        await model.Watch(Project, Id);

        Assert.Equal(2, reads);
        Assert.All(reading, loading => Assert.True(loading));
        Assert.Equal("Ready", model.StateLabel);
    }

    [Fact]
    public async Task AStaleWholeReadsOutOfDateInItsMeasuredGroupWithTheCountsKept()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Stale(Id,
            ProposalParsimonyStaleReason.BaselineChanged));

        Assert.Equal("Out of Date", model.StateLabel);
        Assert.Equal(["Out of Date", "Not Set Up"], model.Groups.Select(group => group.StateLabel));
        Assert.Equal(3, model.Groups[0].Measures.Count);
    }

    [Fact]
    public async Task AnUnknownStaleReasonSaysSomethingChangedNotAHumanJudgment()
    {
        var model = await WatchedAsync(ProposalParsimonyStatusFixtures.Stale(Id, (ProposalParsimonyStaleReason)99));

        Assert.Equal("This advice was measured before something changed: something changed. " +
            "It may no longer be right.", model.Sentence);
        Assert.DoesNotContain("judgment", model.Sentence);
    }

    [Fact]
    public async Task AnUnknownStateAndGroupStateReadAsNotKnownNeverAsDidNotFinish()
    {
        var status = ProposalParsimonyStatusFixtures.Ready(Id) with { State = (ProposalParsimonyState)99 };
        status = status.With(ProposalParsimonyStatusFixtures.Measured() with { State = (ProposalParsimonyGroupState)99 });
        var model = await WatchedAsync(status);

        Assert.Equal("Not Known", model.StateLabel);
        Assert.Equal("Not Known", model.Groups[0].StateLabel);
        Assert.DoesNotMatch(new Regex("did not finish|block|error", RegexOptions.IgnoreCase), AllText(model));
    }

    [Fact]
    public async Task ClosingTheProposalStopsTheReadsAtOnce()
    {
        var reads = 0;
        var delays = new ManualDelays();
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.Running(Id)));
        }, delays.Delay);
        var watching = model.Watch(Project, Id);
        await delays.ReleaseNextAsync(watching);
        Assert.Equal(2, reads);

        model.Stop();
        await watching;
        delays.ReleaseAll();

        Assert.Equal(2, reads);
        Assert.False(model.IsVisible);
        Assert.True(delays.LastWasCancelled);
    }

    [Fact]
    public async Task WatchingTheSameProposalAgainDoesNotStartASecondLoopOrBlankTheSection()
    {
        var reads = 0;
        var delays = new ManualDelays();
        var model = new ProposalParsimonyViewModel((_, _) =>
        {
            reads++;
            return Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                reads < 3 ? ProposalParsimonyStatusFixtures.Running(Id) : ProposalParsimonyStatusFixtures.Ready(Id)));
        }, delays.Delay);
        var first = model.Watch(Project, Id);
        var second = model.Watch(Project, Id);

        Assert.Same(first, second);
        Assert.Equal(1, reads);
        Assert.Equal("Running", model.StateLabel);
        await delays.ReleaseNextAsync(first);
        await delays.ReleaseNextAsync(first);
        await first;
        Assert.Equal(3, reads);
    }

    [Fact]
    public async Task AnotherProposalReplacesTheWatchAndAnOldAnswerNeverLandsOnIt()
    {
        var delays = new ManualDelays();
        var late = new TaskCompletionSource<CommandOutcome<ProposalParsimonyStatus>>();
        var model = new ProposalParsimonyViewModel((request, _) => request.ProposalId == "proposal/first"
            ? late.Task
            : Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(
                ProposalParsimonyStatusFixtures.Ready(request.ProposalId))), delays.Delay);
        var first = model.Watch(Project, "proposal/first");
        await model.Watch(Project, "proposal/second");

        late.SetResult(CommandOutcome<ProposalParsimonyStatus>.Success(
            ProposalParsimonyStatusFixtures.Failed("proposal/first", ProposalParsimonyFailureCategory.TimedOut)));
        await first;

        Assert.Equal("Ready", model.StateLabel);
        Assert.Equal("proposal/second", model.ProposalId);
    }

    private static ProposalParsimonyStatus StatusFor(ProposalParsimonyState state) => state switch
    {
        ProposalParsimonyState.NotSetUp => ProposalParsimonyStatusFixtures.NotSetUp(Id),
        ProposalParsimonyState.NotYetRun => ProposalParsimonyStatusFixtures.NotYetRun(Id),
        ProposalParsimonyState.Queued => ProposalParsimonyStatusFixtures.Waiting(Id),
        ProposalParsimonyState.Running => ProposalParsimonyStatusFixtures.Running(Id),
        ProposalParsimonyState.Ready => ProposalParsimonyStatusFixtures.Ready(Id),
        ProposalParsimonyState.Stale => ProposalParsimonyStatusFixtures.Stale(Id,
            ProposalParsimonyStaleReason.ProposalChanged),
        _ => ProposalParsimonyStatusFixtures.Failed(Id, ProposalParsimonyFailureCategory.TimedOut),
    };

    // A pollable state is read once here and its wait ends the loop, so the wording is what the first read showed.
    private static async Task<ProposalParsimonyViewModel> WatchedAsync(ProposalParsimonyStatus status)
    {
        var model = new ProposalParsimonyViewModel(
            (_, _) => Task.FromResult(CommandOutcome<ProposalParsimonyStatus>.Success(status)), NeverDelay);
        await model.Watch(Project, Id);
        return model;
    }

    private static string AllText(ProposalParsimonyViewModel model) => string.Join("\n",
        new[] { model.StateLabel, model.Sentence, model.Note }
            .Concat(model.Groups.SelectMany(group => new[] { group.Title, group.StateLabel, group.Note }
                .Concat(group.Measures))));

    private static Task NeverDelay(TimeSpan span, CancellationToken cancellationToken) =>
        Task.FromCanceled(new CancellationToken(canceled: true));

    private static Task ImmediateDelay(TimeSpan span, CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Delays that finish only when the test releases them, so no wall clock decides a result.</summary>
    private sealed class ManualDelays
    {
        private readonly Queue<TaskCompletionSource> _pending = new();
        private readonly List<TaskCompletionSource> _all = [];
        private CancellationToken _last;

        public List<TimeSpan> Waits { get; } = [];

        public bool LastWasCancelled => _last.IsCancellationRequested;

        public Task Delay(TimeSpan span, CancellationToken cancellationToken)
        {
            Waits.Add(span);
            _last = cancellationToken;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Enqueue(gate);
            _all.Add(gate);
            return gate.Task.WaitAsync(cancellationToken);
        }

        // Lets one wait finish, then returns once the watch asks for the next wait or has ended.
        public async Task ReleaseNextAsync(Task watching)
        {
            while (_pending.Count == 0) await Task.Yield();
            var before = Waits.Count;
            _pending.Dequeue().SetResult();
            while (Waits.Count == before && !watching.IsCompleted) await Task.Yield();
        }

        public void ReleaseAll()
        {
            foreach (var gate in _all) gate.TrySetResult();
        }
    }
}
