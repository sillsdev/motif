using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Commands.SelectionReading;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ResultsInTextVirtualizationTests
{
    [Theory]
    [InlineData(1040, false)]
    [InlineData(1240, false)]
    [InlineData(1040, true)]
    [InlineData(1240, true)]
    public void SwitchingToAnalyzeTextsFillsTheViewportAfterItsFirstBoundedRead(int width, bool openCard)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = 780;
                var page = workspace.PageModel<TextsPageModel>();
                var cardAnchor = page.ResultsInText.SelectedToken!.Occurrence!;
                if (!openCard) page.ResultsInText.CloseTokenCard();
                page.Tab = TextsTab.Matrix;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                page.Tab = TextsTab.AnalyzeTexts;
                window.UpdateLayout();
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                if (openCard) page.ResultsInText.SelectToken((await page.ResultsInText.ReadOccurrenceAsync(cardAnchor))!);
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    PageScreenshots.Settle(window);
                    await page.ResultsInText.LinePages!.Pending;
                    foreach (var words in panel.GetVisualDescendants().OfType<ProgressiveItemsControl>())
                        await words.PageRefresh;
                }
                var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
                var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => ReferenceEquals(viewer.Content, lines));
                Assert.True(reader.Viewport.Height > 0);
                var realized = lines.GetRealizedContainers().OrderBy(control =>
                    Assert.IsType<SelectionLinePosition>(control.DataContext).Header.Number).ToArray();
                Assert.NotEmpty(realized);
                Assert.Equal(Enumerable.Range(1, realized.Length), realized.Select(control =>
                    Assert.IsType<SelectionLinePosition>(control.DataContext).Header.Number));
                var last = realized[^1];
                var origin = last.TranslatePoint(default, reader);
                Assert.NotNull(origin);
                var bottom = origin!.Value.Y + last.Bounds.Height;
                Assert.True(bottom >= reader.Viewport.Height - 1 ||
                    Assert.IsType<SelectionLinePosition>(last.DataContext).Header == page.ResultsInText.VisibleHeaders.Last(),
                    $"Realized lines end at {bottom}, leaving the {reader.Viewport.Height}-high viewport incomplete.");
                var strips = AnalyzeTextsLayoutTests.Strips(panel);
                foreach (var token in realized.SelectMany(control =>
                             Assert.IsType<SelectionLinePosition>(control.DataContext).Model!.Tokens).Where(token => token.IsWord))
                    Assert.Contains(strips, strip => ReferenceEquals(ResultsInTextPanel.TokenOf(strip), token) &&
                        strip.IsEffectivelyVisible && strip.Bounds.Height > 0);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void LongTextsRealizeOnlyNearbyLinesAndCanOpenADistantWordCard()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var setup = await ReaderSetup.OpenAsync(2000, 1);
            var owner = setup.Owner;
            var panel = setup.Panel;
            var window = setup.Window;
            await SettleAsync(setup);
            var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
            AssertBounded(lines, 2000);
            var boundary = lines.GetRealizedContainers().Select(control =>
                Assert.IsType<SelectionLinePosition>(control.DataContext)).MaxBy(line => line.Header.Number)!;
            Assert.True(boundary.Header.Number < lines.ItemCount);
            var target = Assert.Single(boundary.Model!.Tokens).Occurrence!;
            var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(panel), border =>
                ResultsInTextPanel.TokenOf(border)?.Occurrence == target);
            Assert.True(strip.Focus(NavigationMethod.Directional));
            window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            await SettleAsync(setup);
            var focused = Assert.IsType<WordStripToken>(window.FocusManager!.GetFocusedElement());
            Assert.Equal($"word-{boundary.Header.Number + 1}-0", Assert.IsType<ResultsTokenViewModel>(focused.Tag).Form);
            AssertBounded(lines, 2000);
            var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => ReferenceEquals(viewer.Content, lines));
            reader.Offset = new Vector(0, Math.Max(0, reader.Extent.Height - reader.Viewport.Height));
            await SettleAsync(setup);
            Assert.Contains(lines.GetRealizedContainers(), control =>
                Assert.IsType<SelectionLinePosition>(control.DataContext).Header.Number == 2000);
            AssertBounded(lines, 2000);
            var first = owner.VisibleHeaders[0];
            var token = await owner.ReadOccurrenceAsync(owner.FirstOccurrenceInLine(first)!);
            await owner.OpenTokenCardAsync(token!);
            await SettleAsync(setup);
            var card = Assert.Single(panel.GetVisualDescendants().OfType<WordCard>(),
                card => card.IsEffectivelyVisible);
            Assert.Same(owner.SelectedToken, Assert.Single(card.Document!.Sections.OfType<WordCardAnalysis>()).Token);
            Assert.Equal(token!.Occurrence, owner.SelectedToken!.Occurrence);
            Assert.Same(card, window.FocusManager!.GetFocusedElement());
            var origin = card.TranslatePoint(default, reader);
            Assert.NotNull(origin);
            Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, card.Bounds.Size)));
            AssertBounded(lines, 2000);
        }, TimeSpan.FromSeconds(120));
    }

    [Fact]
    public void ScrollingThroughEveryLineKeepsLineRealizationBounded()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var setup = await ReaderSetup.OpenAsync(2000, 1);
            await SettleAsync(setup);
            var lines = setup.Panel.FindControl<ItemsControl>("TextLineItems")!;
            var reader = Assert.Single(setup.Panel.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => ReferenceEquals(viewer.Content, lines));
            var visited = new HashSet<int>();
            var next = 1;
            while (next <= 2000)
            {
                var height = lines.GetRealizedContainers().Where(container => container.Bounds.Height > 0)
                    .Min(container => container.Bounds.Height);
                var perPage = Math.Max(1, (int)Math.Floor(reader.Viewport.Height / Math.Max(1, height)));
                lines.ScrollIntoView(Math.Min(2000, next + perPage - 1) - 1);
                await SettleAsync(setup);
                foreach (var container in lines.GetRealizedContainers())
                    visited.Add(Assert.IsType<SelectionLinePosition>(container.DataContext).Header.Number);
                for (var retry = 0; retry < 4 && !visited.Contains(next); retry++)
                {
                    lines.ScrollIntoView(next - 1);
                    await SettleAsync(setup);
                    foreach (var container in lines.GetRealizedContainers())
                        visited.Add(Assert.IsType<SelectionLinePosition>(container.DataContext).Header.Number);
                }
                Assert.Contains(next, visited);
                AssertBounded(lines, 2000);
                Assert.InRange(setup.Owner.LinePages!.RealizedLines.Count(), 1, 48);
                while (visited.Contains(next)) next++;
            }
            Assert.Equal(Enumerable.Range(1, 2000), visited.Order());
            lines.ScrollIntoView(1999);
            await SettleAsync(setup);
            var finalLine = Assert.Single(lines.GetRealizedContainers(), container =>
                Assert.IsType<SelectionLinePosition>(container.DataContext).Header.Number == 2000);
            var origin = finalLine.TranslatePoint(default, reader);
            Assert.NotNull(origin);
            Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, finalLine.Bounds.Size)));
            AssertBounded(lines, 2000);
            Assert.InRange(setup.Owner.LinePages!.RealizedLines.Count(), 1, 48);
        }, TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void ALongSentencePagesItsStripsAndKeyboardNavigationCrossesThePageBoundary()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var setup = await ReaderSetup.OpenAsync(1, 500);
            await SettleAsync(setup);
            var words = Assert.Single(setup.Panel.GetVisualDescendants().OfType<ProgressiveItemsControl>(),
                control => ReferenceEquals(control.FullItemsSource, setup.Owner.LinePages!.RealizedLines.Single().TokenSource));
            Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
            var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(setup.Panel), border =>
                ResultsInTextPanel.TokenOf(border)?.Occurrence?.Index == 19);
            Assert.True(strip.Focus(NavigationMethod.Directional));
            setup.Window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            setup.Window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
            await SettleAsync(setup);
            var focused = Assert.IsType<WordStripToken>(setup.Window.FocusManager!.GetFocusedElement());
            Assert.Equal(20, Assert.IsType<ResultsTokenViewModel>(focused.Tag).Occurrence!.Index);
            Assert.Equal("word-1-20", Assert.IsType<ResultsTokenViewModel>(focused.Tag).Form);
            Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
            var header = Assert.Single(setup.Owner.VisibleHeaders);
            var first = setup.Owner.FirstOccurrenceInLine(header)!;
            var last = await setup.Owner.ReadOccurrenceAsync(first with { Index = 499 });
            Assert.NotNull(last);
            await setup.Owner.OpenTokenCardAsync(last);
            await SettleAsync(setup);
            var card = Assert.Single(setup.Panel.GetVisualDescendants().OfType<WordCard>(),
                card => card.IsEffectivelyVisible);
            Assert.Equal(last!.Occurrence, setup.Owner.SelectedToken!.Occurrence);
            Assert.Same(setup.Owner.SelectedToken, Assert.Single(card.Document!.Sections.OfType<WordCardAnalysis>()).Token);
            Assert.Same(card, setup.Window.FocusManager!.GetFocusedElement());
            Assert.Contains(AnalyzeTextsLayoutTests.Strips(setup.Panel), border =>
                ResultsInTextPanel.TokenOf(border)?.Occurrence == last.Occurrence);
            Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void PageDownMovesByTheReaderViewportLines()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var setup = await ReaderSetup.OpenAsync(64, 1, 1040);
            await SettleAsync(setup);
            var lines = setup.Panel.FindControl<ItemsControl>("TextLineItems")!;
            var reader = Assert.Single(setup.Panel.GetVisualDescendants().OfType<ScrollViewer>(),
                viewer => ReferenceEquals(viewer.Content, lines));
            var first = Assert.IsType<SelectionLinePosition>(lines.ContainerFromIndex(0)?.DataContext);
            var localLinesPerPage = Math.Max(1,
                (int)Math.Floor(reader.Viewport.Height / Math.Max(1, lines.ContainerFromIndex(0)!.Bounds.Height)));
            Assert.InRange(localLinesPerPage, 1, lines.ItemCount - 1);
            var strip = Assert.Single(AnalyzeTextsLayoutTests.Strips(setup.Panel), border =>
                ResultsInTextPanel.TokenOf(border)?.Occurrence == first.Model!.Tokens[0].Occurrence);
            Assert.True(strip.Focus(NavigationMethod.Directional));
            Assert.Same(strip, setup.Window.FocusManager!.GetFocusedElement());
            setup.Window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null);
            await SettleAsync(setup);
            var focused = Assert.IsType<WordStripToken>(setup.Window.FocusManager!.GetFocusedElement());
            Assert.Equal($"word-{localLinesPerPage + 1}-0", Assert.IsType<ResultsTokenViewModel>(focused.Tag).Form);
        }, TimeSpan.FromSeconds(60));
    }

    private static async Task SettleAsync(ReaderSetup setup)
    {
        var lines = setup.Panel.FindControl<ItemsControl>("TextLineItems")!;
        (int Number, Rect Bounds)[] previous = [];
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            setup.Window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (setup.Owner.LinePages is { } pages) await pages.Pending;
            foreach (var words in setup.Panel.GetVisualDescendants().OfType<ProgressiveItemsControl>())
                await words.PageRefresh;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            setup.Window.UpdateLayout();
            var realized = lines.GetRealizedContainers().ToArray();
            var current = realized.Select(container =>
                (Assert.IsType<SelectionLinePosition>(container.DataContext).Header.Number, container.Bounds)).ToArray();
            var expected = realized.SelectMany(container =>
                Assert.IsType<SelectionLinePosition>(container.DataContext).Model!.Tokens).Where(token => token.IsWord).ToArray();
            var strips = AnalyzeTextsLayoutTests.Strips(setup.Panel).ToArray();
            if (current.Length > 0 && current.SequenceEqual(previous) && expected.All(token =>
                    strips.Any(strip => ReferenceEquals(ResultsInTextPanel.TokenOf(strip), token))))
            {
                return;
            }
            previous = current;
        }
        Assert.Fail("The bounded reader pages did not settle to stable line geometry and word strips.");
    }

    private sealed record ReaderSetup(SelectionModelFixture Fixture, ResultsInTextViewModel Owner,
        TextWordsViewModel Words, ResultsInTextPanel Panel, Window Window) : IAsyncDisposable
    {
        public static async Task<ReaderSetup> OpenAsync(int lineCount, int tokensPerLine, int width = 1240)
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var words = new TextWordsViewModel(client, selection, client.ReaderOwner);
            var assess = new AssessViewModel(client, selection);
            var owner = new ResultsInTextViewModel(words, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client, client.ReaderOwner);
            var fixture = new SelectionModelFixture(client);
            await fixture.PublishAsync(new TextWordsResponse([], [new TextLines(Guid.NewGuid(), "Long Text",
                Enumerable.Range(1, lineCount).Select(number => new TextLine(number,
                    Enumerable.Range(0, tokensPerLine).Select(index =>
                        new TextToken($"word-{number}-{index}", $"word-{number}-{index}", null, null)
                        { WordformId = Guid.NewGuid(), OccurrenceIndex = index }).ToArray())).ToArray())], true));
            await owner.SelectionRefresh;
            var panel = new ResultsInTextPanel(owner);
            var window = new Window { Content = panel, Width = width, Height = 780 };
            window.Show();
            return new ReaderSetup(fixture, owner, words, panel, window);
        }

        public async ValueTask DisposeAsync()
        {
            Window.Close();
            await Owner.StopAsync();
            await Words.StopAsync();
            await Fixture.DisposeAsync();
        }
    }

    private static void AssertBounded(ItemsControl lines, int expected)
    {
        Assert.Equal(expected, lines.ItemCount);
        Assert.InRange(lines.GetRealizedContainers().Count(), 1, 31);
    }
}
