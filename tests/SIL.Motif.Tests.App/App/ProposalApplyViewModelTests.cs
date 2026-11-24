using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Support.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalApplyViewModelTests
{
    [UiThreadFact]
    public async Task ApplyingMeasuredDraftRecordsReceiptAndClearsStagingHistory()
    {
        var fake = new FakeCommandClient();
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op/one"]);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [change],
            [new ChangeFit("one", true, [])]));
        fake.MeasurePendingCompletesWith(PendingMeasureResponseFactory.Create("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")), "Applied pending changes.");
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);

        await context.ProposalApply.ApplyCurrentAsync(null);

        Assert.Equal("revision/one", Assert.Single(fake.ApplyPendingRequests).Revision);
        Assert.Equal("draft/one", context.ProposalApply.Receipt!.ProposalId);
        Assert.Empty(context.Changes.Items);
        Assert.True(context.Evidence.AppliedSinceRefresh);
        Assert.NotNull(context.ProposalApply.BaselineRefusal);
        Assert.Equal("There is nothing to undo.", await context.Changes.UndoStagingActionAsync());
        Assert.Equal("There is nothing to redo.", await context.Changes.RedoStagingActionAsync());
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task PendingDraftKeepsFitAndMeasurementBlockers()
    {
        var fake = new FakeCommandClient();
        var change = new PendingChange("one", "wordform/one", "one", "approve", "assessment/one", "reading", ["op/one"]);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [change],
            [new ChangeFit("one", false, ["The word changed."])]));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var remove = new RelayCommand(() => { });

        var blockers = context.ProposalApply.Blockers(null, remove, null, null);

        Assert.Contains(blockers, blocker => blocker.Kind == ApplyBlockerKind.NoLongerFits);
        Assert.Contains(blockers, blocker => blocker.Kind == ApplyBlockerKind.NotMeasured);
        Assert.False(context.ProposalApply.CanApply(null, remove, null, null));
        await context.ProposalApply.ApplyCurrentAsync(null);
        Assert.Empty(fake.ApplyPendingRequests);
        Assert.Empty(fake.MeasurePendingRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ALostApprovedAnalysisDisablesApplyBeforeAnyClick()
    {
        var fake = new FakeCommandClient();
        static PendingChange Change(string id) =>
            new(id, "wordform/" + id, id, "approve", "assessment/one", "reading", ["op/" + id]);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept"), Change("gone"), Change("unsure")],
            [new ChangeFit("kept", true, []), new ChangeFit("gone", false, ["The word changed."]),
             new ChangeFit("unsure", ChangeFitStatus.Uncertain, ["The source changed."])]));
        fake.MeasurePendingCompletesWith(PendingMeasureResponseFactory.Create("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers with { WordsLosingApprovedAnalysis = ["kept"] }));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null) { FieldWorksHeldProject = true };
        context.RecordApplied();
        var refresh = new RelayCommand(() => { });
        var remove = new RelayCommand(() => { });
        var apply = context.ProposalApply;
        apply.ShowReconciliationNeeded();

        var blockers = apply.Blockers(null, remove, refresh, null);

        Assert.Equal([ApplyBlockerKind.ReconciliationNeeded, ApplyBlockerKind.NoLongerFits,
            ApplyBlockerKind.Uncertain, ApplyBlockerKind.FieldWorksHoldsProject, ApplyBlockerKind.FieldWorksSavedSince,
            ApplyBlockerKind.LosesApprovedAnalysis], blockers.Select(blocker => blocker.Kind));
        Assert.Same(refresh, blockers[0].Action);
        Assert.Same(remove, blockers[1].Action);
        Assert.Same(refresh, blockers[4].Action);
        Assert.False(apply.CanApply(null, remove, refresh, null));
        await apply.ApplyCurrentAsync(null);
        Assert.Empty(fake.ApplyPendingRequests);
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ProposalReadinessMustBelongToTheCurrentTrialAndContent()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("proposal/one", "revision/one", [], []));
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        await context.Changes.TargetAsync("proposal/one");
        var trial = new TrialRecord("job/new", "proposal/one", "intent/one", WorkspaceContextTests.Assessment().Baseline.Token,
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, DateTimeOffset.UtcNow,
            TrialFreshness.Current);
        context.Lens = new ProposalLens("proposal/one", "Proposal", "intent/one", trial, MarkupView.Today);
        var entry = new GrammarChangeEntry("entry", 0, "A change", ["op/one"], [], "content", false,
            "composer", "A change", [], [], []);
        var readiness = new ReadinessProjection(true, false, false, null, [], false);
        var review = new ProposalReviewResponse("proposal/one", "intent/one", "revision/one", [entry],
            trial with { JobId = "job/old" }, readiness, [], 0);
        var remove = new RelayCommand(() => { });

        Assert.Contains(context.ProposalApply.Blockers(review, remove, null, null),
            blocker => blocker.Kind == ApplyBlockerKind.NotReady);
        Assert.False(context.ProposalApply.CanApply(review, remove, null, null));
        await context.ProposalApply.ApplyCurrentAsync(review);
        Assert.Empty(fake.ApplyProposalRequests);
        Assert.True(context.ProposalApply.CanApply(review with { Trial = trial }, remove, null, null));
        Assert.False(context.ProposalApply.CanApply(review with { Trial = trial, IntentDigest = "intent/old" },
            remove, null, null));
        await context.StopProjectWorkAsync();
    }

    [Fact]
    public void WorkspaceOwnsOneApplyAndReconciliationState()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var apply = context.ProposalApply;
        apply.ShowReconciliationNeeded();

        Assert.Same(apply, context.ProposalApply);
        Assert.True(apply.NeedsReconciliation);

        context.ClearProject();

        Assert.False(apply.NeedsReconciliation);
    }

    [Fact]
    public void ReconciliationIsClearedOnlyAfterTheRefreshSettlesIt()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        var apply = new ProposalApplyViewModel(context);
        apply.ShowReconciliationNeeded();

        Assert.True(apply.NeedsReconciliation);
        Assert.Equal(RefusalCodes.ApplyReconciliationNeeded, apply.ApplyRefusal?.Code);
        apply.ClearReconciliationNeeded();
        Assert.Null(apply.ApplyRefusal);

        apply.ApplyRefusal = WindowRefusal.From(new Refusal("apply.other", FailureReason.Refused, "refused"));
        apply.ClearReconciliationNeeded();
        Assert.Equal("apply.other", apply.ApplyRefusal.Code);
    }

    [UiThreadFact]
    public async Task ProjectStopCancelsAndJoinsTheApplyWrite()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.ApplyPendingHandler = async (_, cancellationToken) =>
        {
            started.SetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException();
        };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        var applying = context.ProposalApply.ApplyAsync(() => true, () => null, () => 0, () => 0,
            () => { }, () => Task.CompletedTask, _ => { }, () => { }, () => { });
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await context.StopProjectWorkAsync().WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(token.IsCancellationRequested);
        Assert.True(applying.IsCanceled);
        Assert.False(context.ProposalApply.IsApplying);
    }

    [UiThreadFact]
    public async Task ConcurrentRequestsCannotStartTwoApplyWrites()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [], []));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = new TaskCompletionSource<CommandOutcome<ApplyPendingResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        fake.ApplyPendingHandler = (_, _) => { calls++; started.SetResult(); return result.Task; };
        var context = WorkspaceContextTests.NewContext(fake);
        await context.OpenProjectAsync("project.fwdata");
        Task Apply() => context.ProposalApply.ApplyAsync(() => true, () => null, () => 0, () => 0,
            () => { }, () => Task.CompletedTask, _ => { }, () => { }, () => { });

        var first = Apply();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Apply();
        Assert.Equal(1, calls);
        result.SetResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending));
        await first;
        await context.StopProjectWorkAsync();
    }

    [UiThreadFact]
    public async Task ClearingAProjectStopsWaitingAndDoesNotPublishItsRefreshRefusal()
    {
        var context = WorkspaceContextTests.NewContext(new FakeCommandClient());
        await context.OpenProjectAsync("project.fwdata");
        context.RecordApplied();
        var release = new TaskCompletionSource();
        context.RefreshProjectCommand = new AsyncRelayCommand(() => release.Task);
        var apply = new ProposalApplyViewModel(context);

        var recording = apply.RecordBaselineAsync("project.fwdata");
        Assert.True(apply.IsRecordingBaseline);
        apply.Reset();
        await recording;

        Assert.False(apply.IsRecordingBaseline);
        Assert.Null(apply.BaselineRefusal);
        release.SetResult();
        await context.StopProjectWorkAsync();
    }
}
