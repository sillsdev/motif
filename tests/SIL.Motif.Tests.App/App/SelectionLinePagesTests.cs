using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SelectionLinePagesTests
{
    [Fact]
    public void VirtualizedLinesAndLongLinePagesShareBoundedModelsAndReleaseOnDetach()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var weak = await ScrollPagesAsync();
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsLineViewModel), 0, 0));
            Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(weak, typeof(ResultsTokenViewModel), 0, 0));
        }, TimeSpan.FromSeconds(90));
    }

    private static async Task<IReadOnlyList<WeakReference<object>>> ScrollPagesAsync()
    {
        var textId = Guid.NewGuid();
        var wordId = Guid.NewGuid();
        var lines = Enumerable.Range(1, 100).Select(number => new TextWordsProjectedLine(number,
            $"Sentence {number}", Enumerable.Range(0, number == 1 ? 500 : 6).Select(index =>
                new TextWordsProjectedToken("word", [new WritingSystemText("word", "en")], wordId,
                    "unanalysed", null, null, null, null, index, null)).ToArray(), Guid.NewGuid(),
            Guid.NewGuid(), true)).ToArray();
        using var fixture = new StoredSelectionFixture(new TextWordsProjection([
            new TextWordsProjectedText(textId, "Paged lines", lines, [])],
            [new TextWordsProjectedWordform(wordId, [], [], 0, false, [])]));
        var opened = await fixture.OpenAsync(new OpenSelectionReaderRequest(fixture.ProjectPath, [textId], []),
            CancellationToken.None);
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summarized = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summarized.Succeeded, summarized.Refusal?.Message);
        using var summary = summarized.Value!;
        var weak = new List<WeakReference<object>>();
        var checkedAnchors = new HashSet<SIL.Motif.Contract.Requests.OccurrenceAnchor>();
        var owner = new SelectionLinePages(reader, Assert.Single(summary.Value.Texts),
            () => summary.Lease.RegisterModel(SelectionModelKind.Line), prepare: token =>
            {
                weak.Add(new WeakReference<object>(token));
                token.IsSelectedForActions = token.Occurrence is { } anchor && checkedAnchors.Contains(anchor);
            });
        using var observation = owner.Observe(weak.Add);
        var list = new ItemsControl
        {
            ItemsSource = owner.Headers,
            ItemsPanel = new FuncTemplate<Panel?>(() => new VirtualizingStackPanel()),
            ItemTemplate = new FuncDataTemplate<SelectionLineHeader>((header, _) =>
            {
                if (header is null) return new TextBlock();
                var line = owner.RealizeLine(header);
                var row = new StackPanel
                {
                    MinHeight = 120,
                    DataContext = line,
                    Children =
                    {
                        new TextBlock { Text = $"Line {line.Number}" },
                        new ProgressiveItemsControl
                        {
                            FullItemsSource = line.DisplayTokens,
                            ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel()),
                            ItemTemplate = new FuncDataTemplate<SelectionTokenPosition>((position, _) =>
                            {
                                var token = new TextBlock { Text = position?.Model?.Text, DataContext = position?.Model };
                                token.DetachedFromVisualTree += (_, _) => token.DataContext = null;
                                return token;
                            }),
                        },
                    },
                };
                row.DetachedFromVisualTree += (_, _) => row.DataContext = null;
                return row;
            }),
        };
        var host = new Border { Child = new ScrollViewer { Content = list } };
        _ = new VisibleControlLifetime(host, () =>
        {
            owner.Show();
            list.ItemsSource = owner.Headers;
        }, () =>
        {
            list.ItemsSource = null;
            owner.Clear();
        });
        var window = new Window { Width = 1000, Height = 700, Content = host };
        try
        {
            Assert.Throws<ArgumentException>(() => owner.RealizeLine(owner.Headers[0] with
            {
                ParagraphId = Guid.NewGuid(),
            }));
            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            window.Show();
            await LayoutAsync(window, owner);
            Assert.Null(owner.Refusal);
            var first = owner.Lines[0];
            Assert.Equal(20, first.Tokens.Count);
            var checkedAnchor = first.Tokens[0].Occurrence!;
            checkedAnchors.Add(checkedAnchor);
            var pages = list.GetVisualDescendants().OfType<ProgressiveItemsControl>()
                .First(control => ReferenceEquals(control.FullItemsSource, first.TokenSource));
            var visited = new HashSet<int>();
            for (var offset = 0; offset < 500; offset += 20)
            {
                Assert.Equal(20, first.Tokens.Count);
                var positions = pages.Items.OfType<SelectionTokenPosition>().ToArray();
                Assert.Equal(20, positions.Length);
                Assert.All(positions, position => Assert.NotNull(position.Model));
                foreach (var token in first.Tokens) Assert.True(visited.Add(token.OccurrenceIndex));
                Assert.InRange(reader.Diagnostics.LiveLineModels, 1, 48);
                Assert.InRange(reader.Diagnostics.LiveTokenModels, 20, 512);
                Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
                Assert.InRange(reader.Diagnostics.LeasedLineTokens, 20, 512);
                if (offset == 480) break;
                var next = pages.GetVisualDescendants().OfType<Button>()
                    .Single(button => button.Content?.ToString()?.StartsWith("Show ", StringComparison.Ordinal) == true);
                next.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await pages.PageRefresh;
                await LayoutAsync(window, owner);
                Assert.All(positions, position => Assert.Null(position.Model));
            }
            Assert.Equal(500, visited.Count);
            for (var index = 0; index < 100; index += 10)
            {
                await ShowLineAsync(list, index, window, owner);
                Assert.Null(owner.Refusal);
                Assert.InRange(reader.Diagnostics.LiveLineModels, 1, 48);
                Assert.InRange(reader.Diagnostics.LiveTokenModels, 1, 512);
                Assert.InRange(reader.Diagnostics.CachedLineRanges, 1, 3);
                Assert.InRange(reader.Diagnostics.LeasedLineRanges, 1, 2);
                Assert.Equal(1, reader.Diagnostics.TextRowsDeserialized);
            }
            await ShowLineAsync(list, 99, window, owner);
            var lastContainer = list.ContainerFromIndex(99);
            Assert.NotNull(lastContainer);
            var scroll = list.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(owner.RealizedLines.Any(line => line.Number == 100 && line.Tokens.Count > 0),
                $"Last container: {lastContainer}; content: {(lastContainer as ContentControl)?.Content}; " +
                $"scroll: {scroll.Offset}/{scroll.Extent}/{scroll.Viewport}; " +
                $"labels: {string.Join(',', list.GetVisualDescendants().OfType<TextBlock>()
                    .Select(label => label.Text).Where(label => label?.StartsWith("Line ") == true))}; " +
                $"last current source: {owner.Lines[99].TokenSource}; " +
                $"visible source sizes: {string.Join(',', list.GetVisualDescendants().OfType<ProgressiveItemsControl>()
                    .Select(control => (control.FullItemsSource as IProgressivePageSource)?.Count))}; refusal: {owner.Refusal}");
            await ShowLineAsync(list, 0, window, owner);
            Assert.Contains(owner.Lines[0].Tokens, token => token.Occurrence == checkedAnchor &&
                token.IsSelectedForActions);
            var oldSource = (IProgressivePageSource)owner.Lines[0].TokenSource!;
            host.IsVisible = false;
            await owner.Pending;
            Assert.Equal(0, owner.Lines.Count);
            Assert.Equal(0, oldSource.Count);
            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            host.IsVisible = true;
            await ShowLineAsync(list, 0, window, owner);
            Assert.Contains(owner.Lines[0].Tokens, token => token.Occurrence == checkedAnchor &&
                token.IsSelectedForActions);
            window.Content = null;
            await owner.StopAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { },
                Avalonia.Threading.DispatcherPriority.Background);
            Assert.Equal(0, reader.Diagnostics.LiveLineModels);
            Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(0, reader.Diagnostics.LeasedLineRanges);
        }
        finally
        {
            window.Close();
            await owner.StopAsync();
        }
        return weak;
    }

    private static async Task LayoutAsync(Window window, SelectionLinePages owner)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            PageScreenshots.Settle(window);
            await owner.Pending;
            foreach (var control in window.GetVisualDescendants().OfType<ProgressiveItemsControl>().ToArray())
                await control.PageRefresh;
        }
    }

    private static async Task ShowLineAsync(ItemsControl list, int index, Window window, SelectionLinePages owner)
    {
        await owner.ReadLinePageAsync(index + 1, 0);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            list.ScrollIntoView(index);
            var scroll = list.GetVisualAncestors().OfType<ScrollViewer>().First();
            if (index == 0) scroll.ScrollToHome();
            else if (index == owner.Lines.Count - 1) scroll.ScrollToEnd();
            await LayoutAsync(window, owner);
        }
    }
}
