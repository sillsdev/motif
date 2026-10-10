using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SelectionAnalyzeTests
{
    [Fact]
    public void AnalyzePanelPagesEveryLongLineTokenAndReleasesModelsWhenHiddenAndDetached()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var weak = await ScrollAnalyzeAsync();
            await Task.Yield();
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsLineViewModel), 0, 0));
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsTokenViewModel), 0, 0));
        }, TimeSpan.FromSeconds(480));
    }

    [Fact]
    public void AnalyzeShortLinesRealizeAndReleaseWithoutLongLinePaging()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var weak = await ScrollAnalyzeAsync(20, 6);
            await Task.Yield();
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsLineViewModel), 0, 0));
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsTokenViewModel), 0, 0));
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void AnalyzeLongLineReturnsToAnExactHiddenPageBeforeFocusing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var weak = await ScrollAnalyzeAsync(60, 40);
            await Task.Yield();
            ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsLineViewModel), 0, 0);
            ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsTokenViewModel), 0, 0);
        }, TimeSpan.FromSeconds(180));
    }

    [Fact]
    public void OffscreenCheckedScopeFreezesTargetsAndEvidenceBeforeTheFirstWriteCompletes()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var projection = Projection();
            using var fixture = new StoredSelectionFixture(projection);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = 0;
            var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
            client.PendingPutHandler = async (_, _) =>
            {
                if (++calls == 1) { entered.SetResult(); await complete.Task; }
                return SIL.Motif.Contract.Commands.CommandOutcome<PendingChangesSnapshot>.Success(new(null, "none", [], []));
            };
            var selection = new SelectionViewModel(client);
            var reads = new WorkspaceSelection(client);
            var words = new TextWordsViewModel(client, selection, reads);
            var assess = new AssessViewModel(client, selection);
            var changes = new ChangesViewModel(client);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, changes, client, reads);
            try
            {
                await changes.OpenProjectAsync(fixture.ProjectPath);
                await reads.ReloadAsync(fixture.ProjectPath, [projection.Texts[0].TextId], []);
                await owner.SelectionRefresh;
                var expected = reads.Reader!.Context.ExpectedWriteContext();
                owner.SelectAllWordsCommand.Execute(null);
                Assert.Equal(1094, owner.CheckedWordCount);
                Assert.Equal(0, reads.Reader.Diagnostics.CreatedTokenModels);
                var write = owner.MarkSpellingsIncorrectCommand.ExecuteAsync(AnalysisOperationScope.CheckedWords);
                await entered.Task;
                await reads.ReloadAsync(fixture.ProjectPath, [], ["later"]);
                complete.SetResult();
                await write;
                Assert.Equal(2, client.PendingPutRequests.Count);
                Assert.All(client.PendingPutRequests, request => Assert.Equal(expected, request.ExpectedContext));
                Assert.Equal(projection.Wordforms.Select(word => CanonicalId.FromGuid(word.WordformId).Value).Order(),
                    client.PendingPutRequests.Select(request => request.Change.WordformId).Order());
            }
            finally
            {
                complete.TrySetResult();
                await owner.StopAsync();
                await words.StopAsync();
                await reads.StopAsync();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static async Task<IReadOnlyList<WeakReference<object>>> ScrollAnalyzeAsync(int lineCount = 100, int longLineCount = 500)
    {
        var projection = Projection(lineCount, longLineCount);
        using var fixture = new StoredSelectionFixture(projection);
        var client = new FakeCommandClient { SelectionReaderHandler = fixture.OpenAsync };
        var selection = new SelectionViewModel(client);
        var reads = new WorkspaceSelection(client);
        var words = new TextWordsViewModel(client, selection, reads);
        var assess = new AssessViewModel(client, selection);
        var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { }, new ChangesViewModel(client), client, reads);
        var weak = new List<WeakReference<object>>();
        owner.NativeModelCreated += weak.Add;
        var panel = new ResultsInTextPanel(owner);
        var host = new Border { Child = panel };
        var window = new Window { Width = 1240, Height = 780, Content = host };
        try
        {
            await reads.ReloadAsync(fixture.ProjectPath, [projection.Texts[0].TextId], []);
            await owner.SelectionRefresh;
            var expectedCount = longLineCount + (lineCount - 1) * 6;
            Assert.Equal(expectedCount, owner.AllCount);
            Assert.Equal(expectedCount, owner.NotAssessedCount);
            Assert.Equal(0, reads.Reader!.Diagnostics.CreatedTokenModels);
            Assert.Equal(0, reads.Reader.Diagnostics.CreatedLineModels);
            owner.SelectAllWordsCommand.Execute(null);
            window.Show();
            await LayoutAsync(window, owner);
            var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
            var first = owner.LinePages!.RealizedLines.Single(line => line.Number == 1);
            var pages = panel.GetVisualDescendants().OfType<ProgressiveItemsControl>().Single(control =>
                ReferenceEquals(control.FullItemsSource, first.DisplayTokens));
            var firstStrip = panel.GetVisualDescendants().OfType<Border>().First(border => border.Name == "WordStrip");
            Assert.Same(first.Tokens[0], Assert.IsType<ResultsTokenViewModel>(firstStrip.Tag));
            Assert.True(firstStrip.Bounds.Height > 0);
            var cardSource = first.Tokens[0];
            owner.SelectToken(cardSource);
            Assert.NotSame(cardSource, owner.SelectedToken);
            Assert.True(cardSource.IsCardOpen);
            Assert.Equal(1, reads.Reader.Diagnostics.LiveCardModels);
            Assert.Equal(1, reads.Reader.Diagnostics.LivePinnedModels);
            owner.CloseTokenCard();
            Assert.False(cardSource.IsCardOpen);
            Assert.Equal(0, reads.Reader.Diagnostics.LiveCardModels);
            Assert.Equal(0, reads.Reader.Diagnostics.LivePinnedModels);
            var seen = new HashSet<int>();
            for (var page = 0; page < (longLineCount + 19) / 20; page++)
            {
                await LayoutAsync(window, owner);
                Assert.Equal(Math.Min(20, longLineCount - page * 20), first.Tokens.Count);
                Assert.All(first.Tokens, token => Assert.True(token.IsSelectedForActions));
                foreach (var token in first.Tokens) Assert.True(seen.Add(token.OccurrenceIndex));
                if ((page + 1) * 20 < longLineCount) pages.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content is string label && label.StartsWith("Show ", StringComparison.Ordinal))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.InRange(reads.Reader.Diagnostics.LiveLineModels, 1, 48);
                Assert.InRange(reads.Reader.Diagnostics.LiveTokenModels, 1, 512);
            }
            Assert.Equal(longLineCount, seen.Count);
            var uncheckedAnchor = first.Tokens[0].Occurrence!;
            first.Tokens[0].IsSelectedForActions = false;
            Assert.Equal(expectedCount - 1, owner.CheckedWordCount);
            var viewer = panel.FindControl<ScrollViewer>("TextScrollViewer")!;
            for (var index = 1; index < lineCount; index++)
            {
                await owner.LinePages!.ReadLinePageAsync(index + 1, 0);
                // Scrolled as a reader scrolls: a jump to an unrealized line rebuilds every visible line's strips.
                while (lines.ContainerFromIndex(index) is null &&
                       viewer.Offset.Y < viewer.Extent.Height - viewer.Viewport.Height)
                {
                    viewer.Offset = viewer.Offset.WithY(viewer.Offset.Y + viewer.Viewport.Height / 2);
                    PageScreenshots.Settle(window);
                }
                if (index == lineCount - 1) viewer.ScrollToEnd();
                await LayoutAsync(window, owner);
                Assert.NotNull(lines.ContainerFromIndex(index));
                Assert.InRange(reads.Reader.Diagnostics.LiveLineModels, 1, 48);
                Assert.InRange(reads.Reader.Diagnostics.LiveTokenModels, 1, 512);
                Assert.InRange(lines.GetRealizedContainers().Count(), 1, 48);
            }
            Assert.NotNull(lines.ContainerFromIndex(lineCount - 1));
            Assert.Equal(expectedCount - 1, owner.CheckedWordCount);
            Assert.Equal(1, reads.Reader.Diagnostics.TextRowsDeserialized);
            host.IsVisible = false;
            PageScreenshots.Settle(window);
            Assert.Equal(0, reads.Reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reads.Reader.Diagnostics.LiveLineModels);
            host.IsVisible = true;
            Assert.True(await panel.FocusOccurrenceAsync(uncheckedAnchor));
            await LayoutAsync(window, owner);
            var restored = owner.LinePages!.RealizedLines.Single(line => line.Number == 1).Tokens
                .Single(token => token.Occurrence == uncheckedAnchor);
            Assert.False(restored.IsSelectedForActions);
            Assert.Equal(expectedCount - 1, owner.CheckedWordCount);
            window.Content = null;
            PageScreenshots.Settle(window);
            await owner.StopAsync();
            Assert.Equal(0, reads.Reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reads.Reader.Diagnostics.LiveTokenModels);
            first = null!;
            cardSource = null!;
            restored = null!;
            firstStrip = null!;
            pages = null!;
        }
        finally
        {
            window.Close();
            await owner.StopAsync();
            await words.StopAsync();
            await reads.StopAsync();
        }
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        PageScreenshots.Settle(window);
        return weak;
    }

    private static async Task LayoutAsync(Window window, ResultsInTextViewModel owner)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            PageScreenshots.Settle(window);
            if (owner.LinePages is { } lines) await lines.Pending;
            foreach (var pages in window.GetVisualDescendants().OfType<ProgressiveItemsControl>().ToArray())
                await pages.PageRefresh;
        }
    }

    private static TextWordsProjection Projection(int lineCount = 100, int longLineCount = 500)
    {
        var words = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var lines = Enumerable.Range(1, lineCount).Select(number => new TextWordsProjectedLine(number,
            $"Sentence {number}", Enumerable.Range(0, number == 1 ? longLineCount : 6).Select(index =>
                new TextWordsProjectedToken("word", [new WritingSystemText(index % 2 == 0 ? "first" : "second", "en")],
                    words[index % 2], "unanalysed", null, null, null, null, index, null)).ToArray(),
            Guid.NewGuid(), Guid.NewGuid(), true)).ToArray();
        return new TextWordsProjection([new TextWordsProjectedText(Guid.NewGuid(), "Analyze", lines, [])],
            words.Select(id => new TextWordsProjectedWordform(id, [], [], 0, false, [])).ToArray());
    }
}
