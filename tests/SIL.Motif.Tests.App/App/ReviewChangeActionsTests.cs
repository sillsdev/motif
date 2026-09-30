using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewChangeActionsTests
{
    private const string ProjectPath = @"C:\projects\review-actions.fwdata";

    [Fact]
    public async Task GoToTextOnARowOpensThatRowsWord()
    {
        var (context, page) = await OpenReviewAsync(
        [
            Change("second", "zebra", ChangeKinds.Approve),
            Change("first", "apple", ChangeKinds.Approve),
        ]);
        var capture = new PageRequestCapture(context);
        var rows = Assert.Single(page.ReviewGroups).Items;

        page.GoToTextCommand.Execute(rows.Single(row => row.Word == "zebra"));

        Assert.Equal(["zebra"], capture.Requests.Select(request => request.Word));
        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);
    }

    [Fact]
    public async Task ExpandedContextUsesTheLineForTheExactOccurrence()
    {
        var textId = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var paragraphId = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var otherSegmentId = Guid.Parse("00000003-0000-0000-0000-000000000000");
        var targetSegmentId = Guid.Parse("00000004-0000-0000-0000-000000000000");
        var occurrence = new OccurrenceAnchor(textId, paragraphId, targetSegmentId, 1);
        var (context, page, _) = await OpenReviewWithContextAsync(
            [Change("target", "same", ChangeKinds.Approve, occurrence)],
            textId, paragraphId, otherSegmentId, targetSegmentId);
        var change = Assert.Single(Assert.Single(page.ReviewGroups).Items);
        context.OpenPage(WorkspacePage.Review);

        page.ToggleContextCommand.Execute(change);

        Assert.True(change.IsContextExpanded);
        Assert.Equal(["near", "same"], change.ContextTokens.Select(token => token.Form));
        Assert.Equal(occurrence, change.ContextTokens[1].Occurrence);
        Assert.Equal(WorkspacePage.Review, context.CurrentPage);
    }

    private static async Task<(WorkspaceContext Context, ReviewPageModel Page)> OpenReviewAsync(
        IReadOnlyList<PendingChange> changes)
    {
        var (context, page, _) = await OpenReviewWithContextAsync(changes);
        return (context, page);
    }

    private static async Task<(WorkspaceContext Context, ReviewPageModel Page, TextsPageModel Texts)>
        OpenReviewWithContextAsync(IReadOnlyList<PendingChange> changes, Guid textId = default,
            Guid paragraphId = default, Guid otherSegmentId = default, Guid targetSegmentId = default)
    {
        var fake = new FakeCommandClient();
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId, true, [])).ToArray()));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var page = new ReviewPageModel(context);
        await context.OpenProjectAsync(ProjectPath);
        var texts = new TextsPageModel(context);
        if (textId != Guid.Empty)
        {
            var lines = new[]
            {
                new TextLine(1,
                [
                    Word("elsewhere", 0),
                    Word("same", 1),
                ]) { ParagraphId = paragraphId, SegmentId = otherSegmentId },
                new TextLine(2,
                [
                    Word("near", 0),
                    Word("same", 1),
                ]) { ParagraphId = paragraphId, SegmentId = targetSegmentId },
            };
            texts.ResultsInText.Texts.Add(new ResultsTextViewModel(
                new TextLines(textId, "Sample", lines), new Dictionary<string, AssessmentWordResult>()));
        }

        return (context, page, texts);
    }

    private static TextToken Word(string form, int index) => new(form, form, null, "approved")
    {
        WordformId = Guid.NewGuid(),
        OccurrenceIndex = index,
    };

    private static PendingChange Change(string id, string word, string kind, OccurrenceAnchor? occurrence = null) =>
        new(id, "wordform/" + id, word, kind, "assessment/one", "reading", ["operation/" + id])
        {
            Analyses = [new ReviewAnalysis(new ParserReading([]), ReadingGrade.Candidate, true, true)],
            Occurrence = occurrence,
        };

    private sealed class PageRequestCapture(WorkspaceContext context) : PageModel(context)
    {
        public List<OpenWordRequest> Requests { get; } = [];

        protected override void OnRequested(PageRequest request)
        {
            if (request is OpenWordRequest word) Requests.Add(word);
        }
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(PointerPressedEventArgs trigger,
            IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) => Task.FromResult(allowedEffects);
    }
}
