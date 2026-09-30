using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that Review changes names every reason Apply is blocked, in the order a person clears them, each with the
/// action that clears it, and that the page never claims changes are unapplied after an Apply it could not confirm.
/// </summary>
public sealed class ReviewApplyBlockersTests
{
    private const string ProjectPath = @"C:\projects\blockers.fwdata";

    [Fact]
    public async Task EveryBlockerIsNamedInTheOrderAPersonClearsThem()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("kept"), Gone("gone"), Unsure("unsure")));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var saved = DateTimeOffset.UtcNow;
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null)
        {
            FieldWorksHeldProject = true,
            SourceLastWriteUtc = saved,
            ProjectLastWriteUtc = saved.AddHours(1),
        };
        page.ShowReconciliationNeeded();

        Assert.Equal(
        [
            ApplyBlockerKind.ReconciliationNeeded,
            ApplyBlockerKind.NoLongerFits,
            ApplyBlockerKind.Uncertain,
            ApplyBlockerKind.FieldWorksHoldsProject,
            ApplyBlockerKind.FieldWorksSavedSince,
            ApplyBlockerKind.NotMeasured,
        ], page.ApplyBlockers.Select(blocker => blocker.Kind));
        Assert.Equal("Apply is blocked by 6 things", page.ApplyBlockedTitle);
        Assert.False(page.CanApply);
        Assert.All(page.ApplyBlockers, blocker => Assert.Contains(blocker.Sentence, page.ApplyBlockReason));
    }

    [Fact]
    public async Task NoLongerFitsAndUncertainAreBothCountedNotJustTheFirst()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("kept"), Gone("gone"), Unsure("unsure")));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Equal([ApplyBlockerKind.NoLongerFits, ApplyBlockerKind.Uncertain],
            page.ApplyBlockers.Select(blocker => blocker.Kind));
        Assert.Equal("Apply is blocked by 2 things", page.ApplyBlockedTitle);
        var noLongerFits = page.ApplyBlockers[0];
        Assert.Equal("1 change no longer fits: FieldWorks changed its word since you decided.", noLongerFits.Sentence);
        Assert.Equal("Remove the ones that no longer fit", noLongerFits.ActionText);
        Assert.Same(page.RemoveNonFittingCommand, noLongerFits.Action);
    }

    [Fact]
    public async Task ASingleBlockerIsOneThingAndMeasuringIsItsAction()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("kept")));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        var blocker = Assert.Single(page.ApplyBlockers);
        Assert.Equal(ApplyBlockerKind.NotMeasured, blocker.Kind);
        Assert.Equal("Apply is blocked by 1 thing", page.ApplyBlockedTitle);
        Assert.Equal("Check these changes", blocker.ActionText);
        Assert.Same(page.MeasureCommand, blocker.Action);
    }

    [Fact]
    public async Task MeasuredChangesThatAllFitHaveNoBlockers()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("kept")));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        await page.MeasureCommand.ExecuteAsync(null);

        Assert.Empty(page.ApplyBlockers);
        Assert.Equal(string.Empty, page.ApplyBlockedTitle);
        Assert.False(page.IsApplyBlocked);
        Assert.True(page.CanApply);
    }

    [Fact]
    public async Task ASaveInFieldWorksOffersRefresh()
    {
        var refresh = new AsyncRelayCommand(() => Task.CompletedTask);
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("kept")));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake, refresh);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);
        var saved = DateTimeOffset.UtcNow;
        context.Baseline = new WorkspaceBaseline(true, "", "", "", "", null)
        {
            SourceLastWriteUtc = saved,
            ProjectLastWriteUtc = saved.AddHours(1),
        };

        var blocker = Assert.Single(page.ApplyBlockers);
        Assert.Equal(ApplyBlockerKind.FieldWorksSavedSince, blocker.Kind);
        Assert.Equal("Refresh", blocker.ActionText);
        Assert.Same(refresh, blocker.Action);
    }

    [Fact]
    public async Task AfterAnUnconfirmedApplyThePageSaysFieldWorksMayHaveTheChangesAndOffersRefresh()
    {
        var refresh = new AsyncRelayCommand(() => Task.CompletedTask);
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot(Fits("first"), Fits("second")));
        fake.MeasurePendingCompletesWith(new MeasurePendingResult("job/one", "revision/one",
            FakeCommandClient.CompleteNumbers));
        var context = NewContext(fake, refresh);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        await page.MeasureCommand.ExecuteAsync(null);

        page.ShowReconciliationNeeded();

        Assert.True(page.NeedsReconciliation);
        Assert.Equal("2 changes may already be applied", page.CountText);
        Assert.DoesNotContain("not applied yet", page.CountText);
        Assert.Equal("FieldWorks may already have these changes. Refresh to check.", page.ReconciliationNotice);
        var blocker = Assert.Single(page.ApplyBlockers);
        Assert.Equal(ApplyBlockerKind.ReconciliationNeeded, blocker.Kind);
        Assert.Equal(page.ReconciliationNotice, blocker.Sentence);
        Assert.Same(refresh, blocker.Action);
        Assert.Same(refresh, page.RefreshCommand);
        Assert.Null(page.ShownApplyRefusal);

        page.ClearReconciliationNeeded();

        Assert.False(page.NeedsReconciliation);
        Assert.Equal("2 changes not applied yet", page.CountText);
        Assert.Empty(page.ApplyBlockers);
    }

    [Fact]
    public async Task AnEmptyReviewShowsNoSideCardsAndLinksToAnalyzeTexts()
    {
        var fake = new FakeCommandClient();
        fake.PendingChangesIs(Snapshot());
        var context = NewContext(fake);
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var capture = new TextsRequestCapture(context);

        Assert.False(page.ShowsSideCards);
        Assert.Empty(page.ApplyBlockers);
        page.OpenAnalyzeTextsCommand.Execute(null);

        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
        Assert.Equal(TextsTab.AnalyzeTexts, Assert.Single(capture.Requests).Tab);
    }

    private static PendingChangesSnapshot Snapshot(params (PendingChange Change, ChangeFit Fit)[] items) =>
        new("draft/one", "revision/one", items.Select(item => item.Change).ToArray(),
            items.Select(item => item.Fit).ToArray());

    private static (PendingChange, ChangeFit) Fits(string id) => (Change(id), new ChangeFit(id, true, []));

    private static (PendingChange, ChangeFit) Gone(string id) =>
        (Change(id), new ChangeFit(id, false, ["Wordform wordform/" + id + " was deleted."]));

    private static (PendingChange, ChangeFit) Unsure(string id) =>
        (Change(id), new ChangeFit(id, ChangeFitStatus.Uncertain, ["The words in the source sentence have changed."])
        {
            Uncertainty = new ChangeUncertainty("The words in the source sentence have changed.", [], []),
        });

    private static PendingChange Change(string id) =>
        new(id, "wordform/" + id, id, "approve", "assessment/one", "reading", ["operation/" + id]);

    private static WorkspaceContext NewContext(FakeCommandClient fake, IAsyncRelayCommand? refresh = null)
    {
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        var selection = new SelectionViewModel(fake);
        return new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake))
        {
            RefreshProjectCommand = refresh,
        };
    }

    private sealed class TextsRequestCapture(WorkspaceContext context) : PageModel(context)
    {
        public List<OpenTextsRequest> Requests { get; } = [];

        protected override void OnRequested(PageRequest request)
        {
            if (request is OpenTextsRequest texts) Requests.Add(texts);
        }
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
