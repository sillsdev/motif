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
    public async Task RefreshRechecksChangeFitBeforeApply()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", false, ["Wordform was deleted."])]));
        fake.RecheckCompletesWith(new PendingChangesSnapshot("draft/one", "revision/checked",
            [Change("kept", "first")], [new ChangeFit("kept", ChangeFitStatus.Uncertain,
                ["The words in the source sentence have changed."])]));
        fake.PendingLoadRequests.Clear();

        await context.PublishBaselineCapturedAsync();

        Assert.Equal("revision/one", Assert.Single(fake.PendingRecheckRequests).ExpectedRevision);
        Assert.Empty(fake.PendingLoadRequests);
        Assert.True(Assert.Single(context.Changes.Items).IsUncertain);
        Assert.False(page.CanApply);
    }

    [Fact]
    public async Task UncertainChangesHaveTheirOwnPlainApplyBlockReason()
    {
        var uncertain = new ChangeFit("uncertain", ChangeFitStatus.Uncertain,
            ["The words in the source sentence have changed."])
        {
            Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.",
                [new OccurrenceWordToken(0, "wordform/first", "first")],
                [new OccurrenceWordToken(0, "wordform/changed", "changed")]),
        };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("uncertain", "first")], [uncertain]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);

        await context.OpenProjectAsync(ProjectPath);

        var item = Assert.Single(context.Changes.Items);
        Assert.Equal("Uncertain — check again", item.FitStatus);
        Assert.Empty(page.ReviewableChanges);
        Assert.Same(item, Assert.Single(page.UncertainChanges));
        Assert.True(Assert.Single(item.AfterWords, word => word.Form == "changed").IsChanged);
        Assert.False(page.CanApply);
        Assert.Equal("1 change needs another look because its sentence changed. Check it again or undo it.",
            page.ApplyBlockReason);
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

    [Fact]
    public void ReviewExposesACommandForReconcheckingOneUncertainChange()
    {
        Assert.NotNull(typeof(ReviewPageModel).GetProperty("ReconfirmChangeCommand"));
    }

    [Fact]
    public async Task CheckAgainAndUndoTargetTheSelectedUncertainChange()
    {
        var uncertain = new ChangeFit("uncertain", ChangeFitStatus.Uncertain,
            ["The words in the source sentence have changed."])
        {
            Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.",
                [new OccurrenceWordToken(0, "wordform/first", "first")],
                [new OccurrenceWordToken(0, "wordform/changed", "changed")]),
        };
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("uncertain", "first")], [uncertain]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        fake.ReconfirmCompletesWith(new PendingChangesSnapshot("draft/one", "revision/two",
            [Change("uncertain", "first")], [new ChangeFit("uncertain", true, [])]));

        await page.ReconfirmChangeCommand.ExecuteAsync(Assert.Single(page.UncertainChanges));

        Assert.Equal("uncertain", Assert.Single(fake.PendingReconfirmRequests).ChangeId);
        Assert.True(Assert.Single(context.Changes.Items).Fit?.StillFits);
        await context.Changes.RemoveCommand.ExecuteAsync(Assert.Single(context.Changes.Items));
        Assert.Equal("uncertain", Assert.Single(fake.PendingRemoveRequests).ChangeId);
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
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);
        Assert.True(page.CanApply);

        var saved = DateTimeOffset.UtcNow;
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null)
        {
            SourceLastWriteUtc = saved,
            ProjectLastWriteUtc = saved.AddHours(1),
        };

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
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        Assert.False(page.CanApply);
        Assert.Equal("Apply to FieldWorks project", page.ApplyButtonText);
        await page.MeasureCommand.ExecuteAsync(null);

        Assert.True(page.CanApply);
        Assert.Equal(["first"], Assert.Single(fake.MeasurePendingRequests).Words);
        Assert.Contains("kept their approved analyses", page.NumbersText);
    }

    [Theory]
    [InlineData(new[] { "first" }, "Applying these changes would lose an approved analysis for 1 word: first. " +
        "Change or remove the changes that cause it before applying.")]
    [InlineData(new[] { "first", "second" }, "Applying these changes would lose an approved analysis for 2 words: " +
        "first, second. Change or remove the changes that cause it before applying.")]
    public async Task ALostApprovedAnalysisDisablesApplyBeforeAnyClick(string[] lost, string reason)
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first"), Change("other", "second")],
            [new ChangeFit("kept", true, []), new ChangeFit("other", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers with { WordsLosingApprovedAnalysis = lost }));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.False(page.CanApply);
        Assert.False(page.ApplyCommand.CanExecute(null));
        Assert.Equal(reason, page.ApplyBlockReason);
    }

    [Fact]
    public async Task ChangingTheChangesClearsTheLostAnalysisReasonUntilTheyAreCheckedAgain()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers with { WordsLosingApprovedAnalysis = ["first"] }));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        var raised = new List<string?>();
        page.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/two",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        await context.Changes.ReloadAsync();

        Assert.Equal("See what applying does to the numbers before applying.", page.ApplyBlockReason);
        Assert.Contains(nameof(ReviewPageModel.ApplyBlockReason), raised);
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

        Assert.Equal("The check was cancelled.", page.MeasurementRefusal?.Sentence);
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
            page.MeasurementRefusal?.Sentence);
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
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingRefusal = new Refusal("job.wait-timeout", FailureReason.Busy,
            "The wait expired.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);
        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Equal("The check took too long and was stopped. Your changes are unchanged; try applying again.",
            page.ApplyRefusal?.Sentence);
    }

    [Fact]
    public async Task OpeningAnotherProjectCancelsApplyAndDiscardsTheOldProjectResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one", FakeCommandClient.CompleteNumbers));
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
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")),
            "Applied pending changes.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(context.Changes.Items);
        Assert.Equal("draft/one", page.Receipt!.ProposalId);
        Assert.Equal("revision/one", Assert.Single(fake.ApplyPendingRequests).Revision);
        Assert.True(context.Evidence.AppliedSinceRefresh);
    }

    [Fact]
    public async Task AnApplyThatFoundNothingPendingShowsNoReceiptAndLeavesTheProjectUnchanged()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingHandler = (_, _) =>
            Task.FromResult(CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Null(page.Receipt);
        Assert.False(page.HasReceipt);
        Assert.Null(page.ApplyRefusal);
        Assert.False(context.Evidence.AppliedSinceRefresh);
    }

    [Fact]
    public async Task ARegressionRefusalExplainsTheWorseResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
        fake.ApplyPendingRefusal = new Refusal("apply.regression", FailureReason.Refused,
            "Approved readings matched less often.");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("worse results", page.ApplyRefusal?.Sentence);
    }

    [Fact]
    public void AnAnalysisMissingFromTheProjectUsesTheNotPresentLabel()
    {
        var viewModel = new ReviewAnalysisViewModel(
            new ReviewAnalysis(new ParserReading([]), "no-opinion", false, false), "approve");

        Assert.Equal("Not present", viewModel.Opinion);
    }

    [Fact]
    public async Task FieldWorksHoldingTheProjectBlocksApplyAndKeepEditingReturnsToTheOrigin()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first") with { OriginPage = "Warnings" }],
            [new ChangeFit("kept", true, [])]));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult(
            "job/one", "revision/one", FakeCommandClient.CompleteNumbers));
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
