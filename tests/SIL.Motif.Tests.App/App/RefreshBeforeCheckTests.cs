using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A Trial or a Review check that follows an Apply refreshes the project first, because the Apply moved the
/// project past its Baseline; one that follows nothing does not.
/// </summary>
public sealed class RefreshBeforeCheckTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string ProposalId = "proposal/one";

    private static readonly BaselineToken Token = new(
        "project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z", "sha256:" + new string('b', 64));

    [Fact]
    public async Task ATrialAfterAnApplyRefreshesFirstAndSaysTryingWhileItRuns()
    {
        // The window publishes Trial states through the context it was made on, which a pool thread is not.
        await SingleThreadPump.RunAsync(async () =>
        {
            var (fake, context) = await OpenProposalAsync();
            var refresh = new RefreshProbe(context) { Gate = NewGate() };
            context.RefreshProjectCommand = refresh.Command;
            context.RecordApplied();

            var trying = context.TryAgainAsync(ProposalId, CancellationToken.None);

            Assert.Equal(1, refresh.Runs);
            Assert.True(context.TrialRun.IsRunning(ProposalId));
            Assert.Equal(TrialStanding.Trying, context.ProposalStanding);
            Assert.Empty(fake.EnqueueTrialRequests);
            refresh.Gate.SetResult();
            var outcome = await trying;

            Assert.True(outcome.Succeeded);
            Assert.Equal(1, refresh.Runs);
            Assert.Equal(ProposalId, Assert.Single(fake.EnqueueTrialRequests).ProposalId);
            Assert.False(context.Evidence.AppliedSinceRefresh);
            Assert.Equal(ProposalId, context.Lens?.ProposalId);
            Assert.False(context.TrialRun.IsActive);
        });
    }

    [Fact]
    public async Task ATrialWithNothingAppliedSinceTheRefreshDoesNotRefresh()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;

        await context.TryAgainAsync(ProposalId, CancellationToken.None);

        Assert.Equal(0, refresh.Runs);
        Assert.Single(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task ARefreshThatDoesNotCaptureABaselineStopsTheTrialWithATrueMessage()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context) { Captures = false };
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();

        var outcome = await context.TryAgainAsync(ProposalId, CancellationToken.None);

        Assert.Equal(WindowRefusal.RefreshFailedCode, outcome.Refusal?.Code);
        Assert.Empty(fake.EnqueueTrialRequests);
        Assert.Empty(fake.WaitForJobRequests);
        Assert.False(context.TrialRun.IsRunning(ProposalId));
        Assert.Equal("The project could not be refreshed, so nothing was checked. Refresh the project, then try again.",
            context.TrialRun.StateFor(ProposalId).ShownRefusal?.Sentence);
        Assert.True(context.Evidence.AppliedSinceRefresh);
        Assert.Equal(ProposalId, context.Lens?.ProposalId);
    }

    [Fact]
    public async Task ARefreshThatThrowsStopsTheTrial()
    {
        var (fake, context) = await OpenProposalAsync();
        context.RefreshProjectCommand = new AsyncRelayCommand(() => throw new IOException("disk gone"));
        context.RecordApplied();

        var outcome = await context.TryAgainAsync(ProposalId, CancellationToken.None);

        Assert.Equal(WindowRefusal.RefreshFailedCode, outcome.Refusal?.Code);
        Assert.Empty(fake.EnqueueTrialRequests);
        Assert.False(context.TrialRun.IsRunning(ProposalId));
    }

    [Fact]
    public async Task ARefreshThatCannotStartStopsTheTrial()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context) { CanRun = false };
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();

        var outcome = await context.TryAgainAsync(ProposalId, CancellationToken.None);

        Assert.Equal(WindowRefusal.RefreshUnavailableCode, outcome.Refusal?.Code);
        Assert.Equal("The project cannot be refreshed while other work is running, so nothing was checked. Try again when it has finished.",
            context.TrialRun.StateFor(ProposalId).ShownRefusal?.Sentence);
        Assert.Equal(0, refresh.Runs);
        Assert.Empty(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task AWindowWithoutARefreshActionStopsTheTrialAfterAnApply()
    {
        var (fake, context) = await OpenProposalAsync();
        context.RecordApplied();

        var outcome = await context.TryAgainAsync(ProposalId, CancellationToken.None);

        Assert.Equal(WindowRefusal.RefreshUnavailableCode, outcome.Refusal?.Code);
        Assert.Empty(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task CancellingWhileTheRefreshRunsStopsTheTrialAsCancelled()
    {
        // The window publishes Trial states through the context it was made on, which a pool thread is not.
        await SingleThreadPump.RunAsync(async () =>
        {
            var (fake, context) = await OpenProposalAsync();
            var refresh = new RefreshProbe(context) { Gate = NewGate() };
            context.RefreshProjectCommand = refresh.Command;
            context.RecordApplied();
            using var cancel = new CancellationTokenSource();

            var trying = context.TryAgainAsync(ProposalId, cancel.Token);
            await cancel.CancelAsync();
            var outcome = await trying;

            Assert.Equal(WindowRefusal.RefreshCancelledCode, outcome.Refusal?.Code);
            Assert.Equal("Cancelled, so nothing was checked. The Refresh already started carries on until it finishes.",
                WindowRefusal.From(outcome.Refusal!).Sentence);
            Assert.Equal(FailureReason.Cancelled, outcome.Refusal?.Reason);
            Assert.Empty(fake.EnqueueTrialRequests);
            Assert.False(context.TrialRun.IsRunning(ProposalId));
            refresh.Gate.SetResult();
        });
    }

    [Fact]
    public async Task ARefreshAlreadyRunningIsJoinedNotStartedAgain()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context) { Gate = NewGate() };
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();
        var pressed = refresh.Command.ExecuteAsync(null);

        var trying = context.TryAgainAsync(ProposalId, CancellationToken.None);
        refresh.Gate.SetResult();
        await pressed;
        var outcome = await trying;

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, refresh.Runs);
        Assert.Single(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task TheProposalBarsTryAgainRefreshesFirstAfterAnApply()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();
        var bar = new ProposalBarViewModel(context);

        await bar.TryAgainCommand.ExecuteAsync(null);

        Assert.Equal(1, refresh.Runs);
        Assert.Single(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task AFailedRefreshShowsOnTheProposalBarAndQueuesNothing()
    {
        var (fake, context) = await OpenProposalAsync();
        context.RefreshProjectCommand = new RefreshProbe(context) { Captures = false }.Command;
        context.RecordApplied();
        var bar = new ProposalBarViewModel(context);

        await bar.TryAgainCommand.ExecuteAsync(null);

        Assert.Empty(fake.EnqueueTrialRequests);
        Assert.Contains("could not be refreshed", bar.Sentence);
    }

    [Fact]
    public async Task TheReviewPagesTryAgainRefreshesFirstAfterAnApply()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();
        var page = new ReviewPageModel(context);

        await page.TryAgainCommand.ExecuteAsync(null);

        Assert.Equal(1, refresh.Runs);
        Assert.Single(fake.EnqueueTrialRequests);
    }

    [Fact]
    public async Task AMeasureAfterAnApplyRefreshesFirstAndMeasuresTheReloadedChanges()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Pending("revision/before"));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var refresh = new RefreshProbe(context)
        {
            During = () => fake.PendingChangesIs(Pending("revision/after")),
            AfterCapture = () => context.Changes.ReloadAsync(),
        };
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/after", FakeCommandClient.CompleteNumbers));

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(1, refresh.Runs);
        Assert.Equal("revision/after", Assert.Single(fake.MeasurePendingRequests).Revision);
        Assert.Null(page.MeasurementRefusal);
        Assert.True(page.CanApply);
    }

    [Fact]
    public async Task AMeasureWithNothingAppliedSinceTheRefreshDoesNotRefresh()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Pending("revision/one"));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(0, refresh.Runs);
        Assert.Single(fake.MeasurePendingRequests);
    }

    [Fact]
    public async Task AMeasureAfterAnApplyStopsWhenTheRefreshFailsAndMeasuresNothing()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Pending("revision/one"));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        context.RefreshProjectCommand = new RefreshProbe(context) { Captures = false }.Command;
        context.RecordApplied();

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Empty(fake.MeasurePendingRequests);
        Assert.Equal(WindowRefusal.RefreshFailedCode, page.MeasurementRefusal?.Code);
        Assert.False(page.IsMeasuring);
        Assert.False(page.CanApply);
    }

    [Fact]
    public async Task ApplyingAgainAfterAnApplyWaitsForTheCheckThatRefreshesAndNeverAppliesOnAStaleBaseline()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Pending("revision/one"));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")),
            "Applied pending changes.");
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var appliesWhenRefreshed = -1;
        var refresh = new RefreshProbe(context) { During = () => appliesWhenRefreshed = fake.ApplyPendingRequests.Count };
        await page.MeasureCommand.ExecuteAsync(null);
        // Apply's own Refresh stops with no Refresh to run, leaving the Baseline stale for the next check.
        await page.ApplyCommand.ExecuteAsync(null);
        context.RefreshProjectCommand = refresh.Command;
        Assert.True(context.Evidence.AppliedSinceRefresh);
        Assert.Equal(0, refresh.Runs);
        fake.PendingChangesIs(Pending("revision/two"));
        await context.Changes.ReloadAsync();

        Assert.False(page.ApplyCommand.CanExecute(null));
        Assert.Contains("Refresh before applying", page.ApplyBlockReason);
        await page.ApplyCommand.ExecuteAsync(null);
        Assert.Single(fake.ApplyPendingRequests);
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/two", "revision/two", FakeCommandClient.CompleteNumbers));
        await page.MeasureCommand.ExecuteAsync(null);
        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Equal(1, refresh.Runs);
        Assert.Equal(1, appliesWhenRefreshed);
        Assert.Equal(["revision/one", "revision/two"], fake.ApplyPendingRequests.Select(request => request.Revision));
    }

    [Fact]
    public async Task AReRunOfAWordAfterAnApplyDoesNotHideThatTheBaselineIsStale()
    {
        var (fake, context) = await OpenProposalAsync();
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();

        context.PublishEvidence(new WorkspaceEvidence(ReRunAssessment(), DateTimeOffset.UtcNow, WasRerun: true));

        Assert.True(context.Evidence.AppliedSinceRefresh);
        Assert.True(context.Evidence.IsStale);
        await context.TryAgainAsync(ProposalId, CancellationToken.None);
        Assert.Equal(1, refresh.Runs);
        Assert.Single(fake.EnqueueTrialRequests);
        Assert.False(context.Evidence.IsStale);
    }

    [Fact]
    public async Task AMeasureAfterAnApplyAndAReRunOfAWordStillRefreshesFirst()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Pending("revision/one"));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        var context = WorkspaceContextTests.NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var refresh = new RefreshProbe(context);
        context.RefreshProjectCommand = refresh.Command;
        context.RecordApplied();
        context.PublishEvidence(new WorkspaceEvidence(ReRunAssessment(), DateTimeOffset.UtcNow, WasRerun: true));

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal(1, refresh.Runs);
        Assert.Single(fake.MeasurePendingRequests);
    }

    private static AssessCommandResponse ReRunAssessment() => new(
        new BaselineCaptureResponse(Token, "project.fwdata", DateTimeOffset.UnixEpoch, false, false),
        new SelectionProjection([], []), [], "rerun")
    {
        Measurements = [new ProducedAssessmentReference("rerun-assessment", AssessmentKinds.ParseTime, "rerun")],
    };

    private static TaskCompletionSource NewGate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static PendingChangesSnapshot Pending(string revision) => new("draft/one", revision,
        [new PendingChange("kept", "wordform/kept", "first", "approve", "assessment/one", "reading", ["operation/kept"])],
        [new ChangeFit("kept", true, [])]);

    private static async Task<(FakeCommandClient Fake, WorkspaceContext Context)> OpenProposalAsync()
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReadTrialCompletesWith(new TrialRecord("job/trial", ProposalId, "sha256:intent", Token, null,
            new Dictionary<string, string>(), ["dogs"], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.Current));
        await context.OpenProposalAsync(ProposalId, CancellationToken.None);
        return (fake, context);
    }

    // Stands in for the shell's Refresh: it ends the same way whether or not it captured a Baseline.
    private sealed class RefreshProbe
    {
        public RefreshProbe(WorkspaceContext context)
        {
            Command = new AsyncRelayCommand(async () =>
            {
                Runs++;
                During?.Invoke();
                if (Gate is not null) await Gate.Task;
                if (!Captures) return;
                context.Evidence.ClearForNewBaseline();
                if (AfterCapture is not null) await AfterCapture();
            }, () => CanRun);
        }

        public AsyncRelayCommand Command { get; }
        public int Runs { get; private set; }
        public TaskCompletionSource? Gate { get; init; }
        public bool Captures { get; init; } = true;
        public bool CanRun { get; init; } = true;
        public Action? During { get; init; }
        public Func<Task>? AfterCapture { get; init; }
    }
}
