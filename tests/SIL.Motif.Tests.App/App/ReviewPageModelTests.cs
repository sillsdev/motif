using Avalonia.Input;
using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewPageModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string InternalId = "12345678-1234-1234-1234-123456789abc";

    [Fact]
    public async Task RefreshRechecksChangeFitBeforeApply()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", false, ["Wordform was deleted."])]));
        fake.RecheckCompletesWith(new PendingChangesSnapshot("draft/one", "revision/checked",
            [Change("kept", "first")], [new ChangeFit("kept", ChangeFitStatus.Uncertain,
                ["The words in the source sentence have changed."])]));
        fake.PendingLoadRequests.Clear();

        await context.PublishBaselineCapturedAsync();

        Assert.Equal("revision/one", Assert.Single(fake.PendingRecheckRequests).ExpectedRevision);
        Assert.Single(fake.PendingLoadRequests);
        Assert.True(Assert.Single(context.Changes.Items).IsUncertain);
        Assert.False(page.CanApply(null, null, null, null));
    }

    [Fact]
    public async Task RefreshLoadsExternallyStagedChangesBeforeRechecking()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/empty", [], []));
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);
        Assert.Empty(context.Changes.Items);
        fake.PendingLoadRequests.Clear();
        var externallyStaged = Change("external", "first");
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/external",
            [externallyStaged], [new ChangeFit("external", true, [])]));
        fake.RecheckCompletesWith(new PendingChangesSnapshot("draft/one", "revision/refreshed",
            [externallyStaged], [new ChangeFit("external", true, [])]));

        await context.PublishBaselineCapturedAsync();

        Assert.Equal("external", Assert.Single(context.Changes.Items).ChangeId);
        Assert.Equal("revision/external", Assert.Single(fake.PendingRecheckRequests).ExpectedRevision);
        Assert.Single(fake.PendingLoadRequests);
        Assert.Null(context.Changes.ShownRefusal);
        Assert.Equal("revision/refreshed", context.Changes.Snapshot.Revision);
    }

    [Theory]
    [InlineData("The source Segment is gone or no longer resolves uniquely.",
        "The sentence this decision refers to is no longer available.")]
    [InlineData("The source occurrence no longer resolves uniquely.",
        "The word this decision refers to is no longer in the sentence.")]
    [InlineData("The paragraph parse is not current.",
        "FieldWorks has not reparsed this paragraph after the edit.")]
    [InlineData("The paragraph parse was not current when the decision was collected.",
        "FieldWorks had not parsed this paragraph when you made this decision.")]
    [InlineData("The words in the source sentence have changed.",
        "The words in the sentence have changed since you made this decision.")]
    [InlineData(ChangeFitReasons.TrialScratchGone, "The Trial's details were cleared. Run the Trial again to bring them back.")]
    public void UncertainReasonsAreInTheLinguistsWords(string reason, string expected)
    {
        var fit = new ChangeFit("uncertain", ChangeFitStatus.Uncertain, [reason])
        {
            Uncertainty = new ChangeUncertainty(reason, [], []),
        };

        var change = new ChangeViewModel(ChangeKinds.Approve, "kitabu", "reading", fit: fit);

        Assert.Equal(expected, change.UncertaintyReason);
    }

    [Fact]
    public void UncertainActionsNameTheWordForScreenReaders()
    {
        var change = new ChangeViewModel(ChangeKinds.Approve, "kitabu", "reading");
        var checkAgainName = typeof(ChangeViewModel).GetProperty("CheckAgainAutomationName");
        var undoName = typeof(ChangeViewModel).GetProperty("UndoAutomationName");

        Assert.NotNull(checkAgainName);
        Assert.NotNull(undoName);
        Assert.Equal("Check again: kitabu", checkAgainName.GetValue(change));
        Assert.Equal("Undo: kitabu", undoName.GetValue(change));
    }

    [Theory]
    [InlineData("trial.nothing-pending", "There are no changes to check.")]
    [InlineData("apply.nothing-pending", "Motif could not complete this request. Review the project and try again.")]
    public void CheckRefusalsUseTheWindowsWords(string code, string expected) =>
        Assert.Equal(expected, WindowRefusal.From(new Refusal(code, FailureReason.Refused, "detail")).Sentence);

    [Fact]
    public async Task ATimedOutApplyShowsThatTheCheckWasStopped()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingRefusal = new Refusal("job.wait-timeout", FailureReason.Busy,
            "The wait expired.");
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);

        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);
        await page.ApplyCurrentAsync(null);

        Assert.Equal("The check took too long and was stopped. Your changes are unchanged; try applying again.",
            page.ApplyRefusal?.Sentence);
    }

    [Fact]
    public async Task OpeningAnotherProjectCancelsApplyAndDiscardsTheOldProjectResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create("job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.ApplyPendingHandler = async (_, cancellationToken) =>
        {
            started.SetResult(cancellationToken);
            var cancellationSignal = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (await Task.WhenAny(cancellationSignal, release.Task) == cancellationSignal)
            {
                try { await cancellationSignal; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            }
            return CommandOutcome<ApplyPendingResult>.Refused(new Refusal(
                "job.wait-cancelled", FailureReason.Cancelled, "Waiting for the Dry Run was cancelled."));
        };
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);

        var applying = page.ApplyCurrentAsync(null);
        var applyToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.OpenProjectAsync(@"C:\projects\two.fwdata");
        var cancelled = applyToken.IsCancellationRequested;
        release.TrySetResult();
        await applying.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.False(page.IsApplying);
        Assert.Null(page.ApplyRefusal);
        Assert.Contains(fake.PendingLoadRequests,
            request => request.FwDataPath == @"C:\projects\two.fwdata");
    }

    [Fact]
    public async Task ApplyingMeasuredChangesShowsTheReceiptAndEmptiesTheList()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")),
            "Applied pending changes.");
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);
        await context.Changes.PutAsync(new ChangeIntent("added", ChangeKinds.Approve, "wordform/added", "second"));
        var stagedRevision = context.Changes.Snapshot.Revision;
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", stagedRevision, FakeCommandClient.CompleteNumbers));
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCurrentAsync(null);

        Assert.Empty(context.Changes.Items);
        Assert.Equal("draft/one", page.Receipt!.ProposalId);
        Assert.Equal(stagedRevision, Assert.Single(fake.ApplyPendingRequests).Revision);
        Assert.True(context.Evidence.AppliedSinceRefresh);
        Assert.Equal("There is nothing to undo.", await context.Changes.UndoStagingActionAsync());
        Assert.Equal("There is nothing to redo.", await context.Changes.RedoStagingActionAsync());
        Assert.Collection(fake.UsageEntries,
            entry => Assert.Equal("put-pending-change", entry.Command),
            entry => Assert.Equal("trial --pending", entry.Command),
            entry => Assert.Equal("apply --all-pending", entry.Command));
    }

    [Fact]
    public async Task AnApplyThatFoundNothingPendingShowsNoReceiptAndLeavesTheProjectUnchanged()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending));
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCurrentAsync(null);

        Assert.Null(page.Receipt);
        Assert.Null(page.ApplyRefusal);
        Assert.False(context.Evidence.AppliedSinceRefresh);
    }

    [Fact]
    public async Task ARegressionRefusalExplainsTheWorseResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(SIL.Motif.Tests.Support.TestFixtures.PendingMeasureResponseFactory.Create(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingRefusal = new Refusal("apply.regression", FailureReason.Refused,
            "Approved readings matched less often.");
        var context = NewContext(fake);
        var page = context.ProposalApply;
        await context.OpenProjectAsync(ProjectPath);
        await context.PendingMeasurement.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCurrentAsync(null);

        Assert.Contains("worse results", page.ApplyRefusal?.Sentence);
    }

    private static PendingChange Change(string id, string word) =>
        new(id, "wordform/" + id, word, "approve", "assessment/one", "reading", ["operation/" + id]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingOneGroupedReviewChoiceKeepsTheOtherHomograph(bool nonFitting)
    {
        var first = Change("own", "kitabu") with { WordformId = "AAAAAAAAAAAAAAAAAAAAAQ", GroupId = "accepted" };
        var other = Change("other", "kitabu") with { WordformId = "AAAAAAAAAAAAAAAAAAAAAg", GroupId = "accepted" };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", [first, other],
            [new ChangeFit(first.ChangeId, !nonFitting, []), new ChangeFit(other.ChangeId, true, [])]));
        var context = NewContext(fake);
        await context.OpenProjectAsync(ProjectPath);

        await context.Changes.RemoveCommand.ExecuteAsync(context.Changes.Items[0]);

        Assert.Equal("own", Assert.Single(fake.PendingRemoveRequests).ChangeId);
        Assert.Equal("other", Assert.Single(context.Changes.Items).ChangeId);
    }

    private static WorkspaceContext NewContext(FakeCommandClient fake)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
