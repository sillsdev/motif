using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewPageModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private const string InternalId = "12345678-1234-1234-1234-123456789abc";

    [Fact]
    public async Task ADeletedWordformBlocksApplyAndRemovingItKeepsTheOtherChange()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first"), Change("deleted", "second")],
            [new ChangeFit("kept", true, []),
             new ChangeFit("deleted", false, [$"Wordform {InternalId} was deleted in FieldWorks."])]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);

        await context.OpenProjectAsync(ProjectPath);

        Assert.False(page.CanApply);
        Assert.Contains("No longer fits", page.ApplyBlockReason);
        var fitStatus = context.Changes.Items.Single(item => item.ChangeId == "deleted").FitStatus;
        Assert.Equal("No longer fits the current project. Remove this change before review.", fitStatus);
        Assert.DoesNotContain(InternalId, fitStatus);

        await page.RemoveNonFittingCommand.ExecuteAsync(null);

        Assert.Equal("kept", Assert.Single(context.Changes.Items).ChangeId);
        Assert.Equal("kept", Assert.Single(context.Changes.Snapshot.Changes).ChangeId);
    }

    [Fact]
    public async Task RefreshReloadsChangeFitBeforeApply()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", false, ["Wordform was deleted."])]));

        await context.PublishBaselineCapturedAsync();

        Assert.True(Assert.Single(context.Changes.Items).IsNoLongerFits);
        Assert.False(page.CanApply);
    }

    [Fact]
    public async Task CheckAgainReplacesOnlyTheFitsThatPassedTheCommand()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first"), Change("deleted", "second")],
            [new ChangeFit("kept", false, ["Baseline changed."]),
             new ChangeFit("deleted", false, ["Wordform was deleted."])]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        fake.RecheckCompletesWith(new PendingChangesSnapshot("draft/one", "revision/two",
            [Change("kept", "first"), Change("deleted", "second")],
            [new ChangeFit("kept", true, []),
             new ChangeFit("deleted", false, ["Wordform was deleted."])]));

        await page.CheckAgainCommand.ExecuteAsync(null);

        Assert.Equal("revision/one", Assert.Single(fake.PendingRecheckRequests).ExpectedRevision);
        Assert.True(context.Changes.Items.Single(item => item.ChangeId == "kept").Fit?.StillFits);
        Assert.True(context.Changes.Items.Single(item => item.ChangeId == "deleted").IsNoLongerFits);
    }

    [Fact]
    public async Task AFieldWorksSaveBlocksApplyUntilRefresh()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one", "complete", true));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);
        Assert.True(page.CanApply);

        context.CurrentEvidence = new CurrentEvidenceSnapshot("one", DateTimeOffset.UtcNow, null,
            EvidenceFreshness.Stale, null, null, null, null, null);

        Assert.False(page.CanApply);
        Assert.Contains("Refresh", page.ApplyBlockReason);
    }

    [Fact]
    public async Task OpeningReviewDoesNotStartAParserRun()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        _ = new ReviewPageModel(context);

        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Review);

        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.MeasurePendingRequests);
    }

    [Fact]
    public async Task ApplyEnablesOnlyAfterThePersonMeasuresCompleteEvidence()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        Assert.False(page.CanApply);
        Assert.Equal("Apply to FieldWorks project", page.ApplyButtonText);
        await page.MeasureCommand.ExecuteAsync(null);

        Assert.True(page.CanApply);
        Assert.Equal(["first"], Assert.Single(fake.MeasurePendingRequests).Words);
        Assert.Contains("1 search completed", page.NumbersText);
    }

    [Fact]
    public async Task ACancelledMeasurementShowsTheCancellationMessage()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingRefusal = new Refusal("job.wait-cancelled", FailureReason.Cancelled,
            "Waiting for job 'job/one' was cancelled.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal("The check was cancelled.", page.MeasurementError);
    }

    [Fact]
    public async Task ACheckOfChangesThatChangedMeanwhileRefreshesThemAndAsksForAnotherCheck()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingRefusal = new Refusal("trial.changes-changed", FailureReason.Refused,
            "The changes changed. Reload them before measuring.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/two",
            [Change("kept", "first"), Change("added", "second")],
            [new ChangeFit("kept", true, []), new ChangeFit("added", true, [])]));

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal("revision/two", context.Changes.Snapshot.Revision);
        Assert.Equal("The changes were updated while they were being checked. Check them again.",
            page.MeasurementError);
    }

    [Theory]
    [InlineData("trial.nothing-pending", "There are no changes to check.")]
    [InlineData("apply.nothing-pending", "Motif could not complete this request. Review the project and try again.")]
    public void CheckRefusalsUseTheWindowsWords(string code, string expected) =>
        Assert.Equal(expected, UserFacingRefusal.MessageOf(new Refusal(code, FailureReason.Refused, "detail")));

    [Fact]
    public async Task ATimedOutApplyShowsThatTheCheckWasStopped()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ApplyPendingRefusal = new Refusal("job.wait-timeout", FailureReason.Busy,
            "The wait expired.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);
        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("The check took too long and was stopped. Your changes are unchanged; try applying again.",
            page.ApplyError);
    }

    [Fact]
    public async Task OpeningAnotherProjectCancelsApplyAndDiscardsTheOldProjectResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one", "complete", true));
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
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        var applying = page.ApplyCommand.ExecuteAsync(null);
        var applyToken = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await context.OpenProjectAsync(@"C:\projects\two.fwdata");
        var cancelled = applyToken.IsCancellationRequested;
        release.TrySetResult();
        await applying.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(cancelled);
        Assert.False(page.IsApplying);
        Assert.Null(page.ApplyError);
        Assert.Contains(fake.PendingLoadRequests,
            request => request.FwDataPath == @"C:\projects\two.fwdata");
    }

    [Fact]
    public async Task ApplyingMeasuredChangesShowsTheReceiptAndEmptiesTheList()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(context.Changes.Items);
        Assert.Equal("draft/one", page.Receipt!.ProposalId);
        Assert.Equal("revision/one", Assert.Single(fake.ApplyPendingRequests).Revision);
        Assert.True(context.AppliedSinceRefresh);
    }

    [Fact]
    public async Task AnApplyThatFoundNothingPendingShowsNoReceiptAndLeavesTheProjectUnchanged()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ApplyPendingHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Null(page.Receipt);
        Assert.False(page.HasReceipt);
        Assert.Null(page.ApplyError);
        Assert.False(context.AppliedSinceRefresh);
    }

    [Fact]
    public async Task ARegressionRefusalExplainsTheWorseResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ApplyPendingRefusal = new Refusal("apply.regression", FailureReason.Refused,
            "Approved readings matched less often.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("worse results", page.ApplyError);
    }

    [Fact]
    public void AnAnalysisMissingFromTheProjectUsesTheNotStoredYetLabel()
    {
        var viewModel = new ReviewAnalysisViewModel(
            new ReviewAnalysis(new ParserReading([]), "no-opinion", false, false), "approve");

        Assert.Equal("Not stored yet", viewModel.Opinion);
    }

    [Fact]
    public async Task FieldWorksHoldingTheProjectBlocksApplyAndKeepEditingReturnsToTheOrigin()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first") with { OriginPage = "Warnings" }],
            [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Texts);
        context.OpenPage(WorkspacePage.Warnings);
        context.OpenPage(WorkspacePage.Timing);
        context.OpenPage(WorkspacePage.Review);
        await page.MeasureCommand.ExecuteAsync(null);
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null)
        {
            FieldWorksHeldProject = true,
        };

        Assert.False(page.CanApply);
        Assert.Contains("FieldWorks has this project open", page.ApplyBlockReason);
        Assert.False(page.ApplyCommand.CanExecute(null));
        page.KeepEditingCommand.Execute(null);
        Assert.Equal(WorkspacePage.Warnings, context.CurrentPage);
    }

    private static PendingChange Change(string id, string word) =>
        new(id, "wordform/" + id, word, "approve", "assessment/one", "reading", ["operation/" + id]);

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
