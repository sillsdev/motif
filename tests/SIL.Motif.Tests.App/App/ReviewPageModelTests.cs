using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewPageModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Fact]
    public async Task ADeletedWordformBlocksApplyAndRemovingItKeepsTheOtherChange()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first"), Change("deleted", "second")],
            [new ChangeFit("kept", true, []),
             new ChangeFit("deleted", false, ["The wordform was deleted in FieldWorks."])]));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);

        await context.PublishProjectOpenedAsync(ProjectPath);

        Assert.False(page.CanApply);
        Assert.Contains("No longer fits", page.ApplyBlockReason);
        Assert.Contains("deleted", context.Changes.Items.Single(item => item.ChangeId == "deleted").FitStatus);

        await page.RemoveNonFittingCommand.ExecuteAsync(null);

        Assert.Equal("kept", Assert.Single(context.Changes.Items).ChangeId);
        Assert.Equal("kept", Assert.Single(context.Changes.Snapshot.Changes).ChangeId);
    }

    [Fact]
    public async Task OpeningReviewDoesNotStartAParserRun()
    {
        var fake = new FakeCommandClient();
        var context = NewContext(fake);
        _ = new ReviewPageModel(context);

        await context.PublishProjectOpenedAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Review);

        Assert.Empty(fake.AssessRequests);
        Assert.Empty(fake.ReviewTrialRequests);
    }

    [Fact]
    public async Task ApplyEnablesOnlyAfterThePersonMeasuresCompleteEvidence()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.ReviewTrialCompletesWith(new ReviewTrialResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.PublishProjectOpenedAsync(ProjectPath);

        Assert.False(page.CanApply);
        Assert.Equal("Apply to FieldWorks project", page.ApplyButtonText);
        await page.MeasureCommand.ExecuteAsync(null);

        Assert.True(page.CanApply);
        Assert.Equal(["first"], Assert.Single(fake.ReviewTrialRequests).Words);
        Assert.Contains("1 search completed", page.NumbersText);
    }

    [Fact]
    public async Task ApplyingMeasuredChangesShowsTheReceiptAndEmptiesTheList()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.ReviewTrialCompletesWith(new ReviewTrialResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ReviewApplyCompletesWith(new ApplyProjection("draft/one", false, "Applied", [], "sha256:effect",
            new AppliedLogEntrySummary("draft/one", "2026-01-01", "Motif", "sha256:intent")));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.PublishProjectOpenedAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Empty(context.Changes.Items);
        Assert.Equal("draft/one", page.Receipt!.ProposalId);
        Assert.Equal("revision/one", Assert.Single(fake.ReviewApplyRequests).Revision);
        Assert.True(context.AppliedSinceRefresh);
    }

    [Fact]
    public async Task ARegressionRefusalExplainsTheWorseResult()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first")], [new ChangeFit("kept", true, [])]));
        fake.ReviewTrialCompletesWith(new ReviewTrialResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        fake.ReviewApplyRefusal = new Refusal("apply.not-ready", FailureReason.Refused,
            "it would be a regression: approved readings matched less often");
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.PublishProjectOpenedAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        await page.ApplyCommand.ExecuteAsync(null);

        Assert.Contains("worse results", page.ApplyError);
    }

    [Fact]
    public async Task FieldWorksHoldingTheProjectBlocksApplyAndKeepEditingReturnsToTheOrigin()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one",
            [Change("kept", "first") with { OriginPage = "Warnings" }],
            [new ChangeFit("kept", true, [])]));
        fake.ReviewTrialCompletesWith(new ReviewTrialResult(
            "job/one", "revision/one", "1 search completed; 0 incomplete.", true));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.PublishProjectOpenedAsync(ProjectPath);
        context.OpenPage(WorkspacePage.Texts);
        context.OpenPage(WorkspacePage.Warnings);
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
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource());
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
