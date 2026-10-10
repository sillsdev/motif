using Avalonia.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ReviewChangeActionsTests : IAsyncLifetime
{
    private const string ProjectPath = @"C:\projects\review-actions.fwdata";
    private readonly List<StoredSelectionFixture> _fixtures = [];
    private readonly List<WorkspaceContext> _contexts = [];

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
    public async Task ARowsWordRowOpensItsWordInTextAndInTryAWord()
    {
        var (context, page) = await OpenReviewAsync(
        [
            Change("second", "zebra", ChangeKinds.Approve),
            Change("first", "apple", ChangeKinds.Approve),
        ]);
        var capture = new PageRequestCapture(context);
        var zebra = Assert.Single(page.ReviewGroups).Items.Single(row => row.Word == "zebra").Listed!.Row;

        zebra.OpenInTextCommand.Execute(null);
        Assert.Equal(["zebra"], capture.Requests.Select(request => request.Word));
        Assert.Equal(WorkspacePage.Texts, context.CurrentPage);

        zebra.TryWordCommand.Execute(null);
        Assert.Equal(WorkspacePage.TryAWord, context.CurrentPage);
    }

    [Fact]
    public async Task OpeningARowsCardShowsTheLineForTheExactOccurrence()
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

        // Opening the row's card runs this; a second opening keeps the context shown rather than hiding it.
        await page.ShowContextCommand.ExecuteAsync(change);
        var first = change.ContextTokens;
        await page.ShowContextCommand.ExecuteAsync(change);

        Assert.True(change.IsContextExpanded);
        Assert.Equal(["near", "same"], change.ContextTokens.Select(token => token.Form));
        Assert.Same(first, change.ContextTokens);
        Assert.All(change.ContextTokens, token => Assert.IsType<ReviewSentenceToken>(token));
        Assert.Equal(2, context.SelectionReads.Reader!.Diagnostics.LiveTokenModels);
        Assert.Equal(2, context.SelectionReads.Reader.Diagnostics.LivePinnedModels);
        Assert.Equal(WorkspacePage.Review, context.CurrentPage);
    }

    [Fact]
    public async Task ExpandedContextUsesTheLineForTheExactOccurrence()
    {
        var textId = Guid.Parse("00000001-0000-0000-0000-000000000000");
        var paragraphId = Guid.Parse("00000002-0000-0000-0000-000000000000");
        var otherSegmentId = Guid.Parse("00000003-0000-0000-0000-000000000000");
        var targetSegmentId = Guid.Parse("00000004-0000-0000-0000-000000000000");
        var occurrence = new OccurrenceAnchor(textId, paragraphId, targetSegmentId, 1);
        var (context, page, texts) = await OpenReviewWithContextAsync(
            [Change("target", "same", ChangeKinds.Approve, occurrence)],
            textId, paragraphId, otherSegmentId, targetSegmentId);
        var change = Assert.Single(Assert.Single(page.ReviewGroups).Items);
        context.OpenPage(WorkspacePage.Review);
        // Only the context's leases are counted, so other reader work settles first.
        await texts.ResultsInText.SelectionRefresh;
        var reader = context.SelectionReads.Reader!;
        var leasedBefore = reader.Diagnostics.LeasedResults;

        await page.ToggleContextCommand.ExecuteAsync(change);

        Assert.True(change.IsContextExpanded);
        Assert.Equal(["near", "same"], change.ContextTokens.Select(token => token.Form));
        Assert.Equal(occurrence, change.ContextTokens[1].Occurrence);
        Assert.Equal(WorkspacePage.Review, context.CurrentPage);
        await page.ToggleContextCommand.ExecuteAsync(change);
        Assert.Empty(change.ContextTokens);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        Assert.True(reader.Diagnostics.LeasedResults == leasedBefore,
            $"{leasedBefore} leases before the context opened; now {reader.Diagnostics}");
    }

    private async Task<(WorkspaceContext Context, ReviewPageModel Page)> OpenReviewAsync(
        IReadOnlyList<PendingChange> changes)
    {
        var (context, page, _) = await OpenReviewWithContextAsync(changes);
        return (context, page);
    }

    private async Task<(WorkspaceContext Context, ReviewPageModel Page, TextsPageModel Texts)>
        OpenReviewWithContextAsync(IReadOnlyList<PendingChange> changes, Guid textId = default,
            Guid paragraphId = default, Guid otherSegmentId = default, Guid targetSegmentId = default)
    {
        var fake = new FakeCommandClient();
        StoredSelectionFixture? fixture = null;
        if (textId != Guid.Empty)
        {
            var lines = new[]
            {
                new TextWordsProjectedLine(1, "elsewhere same", [Word("elsewhere", 0), Word("same", 1)],
                    paragraphId, otherSegmentId, true),
                new TextWordsProjectedLine(2, "near same", [Word("near", 0), Word("same", 1)],
                    paragraphId, targetSegmentId, true),
            };
            var wordforms = lines.SelectMany(line => line.Tokens).Select(token =>
                new TextWordsProjectedWordform(token.WordformId!.Value, [], [], 0, false, [])).ToArray();
            fixture = new StoredSelectionFixture(new TextWordsProjection(
                [new TextWordsProjectedText(textId, "Sample", lines, [])], wordforms));
            _fixtures.Add(fixture);
            fake.SelectionReaderHandler = fixture.OpenAsync;
        }
        fake.CurrentBaselineCompletesWith(new CurrentBaselineResponse(null, null, false));
        fake.PendingChangesIs(new PendingChangesSnapshot("draft/one", "revision/one", changes,
            changes.Select(change => new ChangeFit(change.ChangeId, true, [])).ToArray()));
        var selection = new SelectionViewModel(fake);
        var context = new WorkspaceContext(selection, new AssessViewModel(fake, selection),
            new ChangesViewModel(fake), fake, new FolderPicker(), new DragSource(), new BaselineViewModel(fake));
        var page = new ReviewPageModel(context);
        _contexts.Add(context);
        await context.OpenProjectAsync(ProjectPath);
        var texts = new TextsPageModel(context);
        if (fixture is not null)
        {
            await context.SelectionReads.ReloadAsync(ProjectPath, [], []);
            Assert.Null(context.SelectionReads.Refusal);
            Assert.NotNull(context.SelectionReads.Reader);
        }
        Assert.Empty(texts.ResultsInText.Texts);

        return (context, page, texts);
    }

    private static TextWordsProjectedToken Word(string form, int index) =>
        new(form, [new WritingSystemText(form, "en")], Guid.NewGuid(), "unanalysed", null, null, null, null, index, null)
            { TextWritingSystem = "en" };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var context in _contexts) await context.StopProjectWorkAsync();
        foreach (var fixture in _fixtures) fixture.Dispose();
    }

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
