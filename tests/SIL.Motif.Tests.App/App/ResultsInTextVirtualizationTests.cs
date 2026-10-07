using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
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
    public void SwitchingToAnalyzeTextsRealizesTheWholeViewportInItsFirstLayout(int width, bool openCard)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                window.Width = width;
                window.Height = 780;
                var page = workspace.PageModel<TextsPageModel>();
                if (!openCard) page.ResultsInText.CloseTokenCard();
                page.Tab = TextsTab.Matrix;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);

                page.Tab = TextsTab.AnalyzeTexts;
                window.UpdateLayout();
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var lines = panel.FindControl<ItemsControl>("TextLineItems")!;
                var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => ReferenceEquals(viewer.Content, lines));
                Assert.True(reader.Viewport.Height > 0);
                var realized = lines.GetRealizedContainers().OrderBy(control =>
                    Assert.IsType<ResultsLineViewModel>(control.DataContext).Number).ToArray();
                Assert.NotEmpty(realized);
                Assert.Equal(Enumerable.Range(1, realized.Length), realized.Select(control =>
                    Assert.IsType<ResultsLineViewModel>(control.DataContext).Number));
                var last = realized[^1];
                var origin = last.TranslatePoint(default, reader);
                Assert.NotNull(origin);
                var bottom = origin!.Value.Y + last.Bounds.Height;
                Assert.True(bottom >= reader.Viewport.Height - 1 ||
                    ReferenceEquals(last.DataContext, page.ResultsInText.VisibleLines.Last()),
                    $"Realized lines end at {bottom}, leaving the {reader.Viewport.Height}-high viewport incomplete.");
                var strips = AnalyzeTextsLayoutTests.Strips(panel);
                foreach (var token in realized.SelectMany(control =>
                             Assert.IsType<ResultsLineViewModel>(control.DataContext).Tokens).Where(token => token.IsWord))
                    Assert.Contains(strips, strip => ReferenceEquals(strip.Tag, token) &&
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
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var texts = new TextWordsViewModel(client, selection);
            var assess = new AssessViewModel(client, selection);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client);
            var text = new ResultsTextViewModel(new TextLines(Guid.NewGuid(), "Long Text",
                Enumerable.Range(1, 2000).Select(number => new TextLine(number,
                    [new TextToken($"word-{number}", $"word-{number}", null, null)])).ToArray()),
                new Dictionary<string, AssessmentWordResult>());
            inText.Texts.Add(text);
            inText.SelectedText = text;
            var panel = new ResultsInTextPanel(inText);
            var window = new Window { Content = panel, Width = 1240, Height = 780 };
            window.Show();
            try
            {
                PageScreenshots.Settle(window);
                var lines = Assert.Single(panel.GetVisualDescendants().OfType<ItemsControl>(),
                    control => ReferenceEquals(control.ItemsSource, inText.VisibleLines));
                AssertBounded(lines);
                var boundary = lines.GetRealizedContainers().Select(control =>
                    Assert.IsType<ResultsLineViewModel>(control.DataContext)).MaxBy(line => line.Number)!;
                Assert.True(boundary.Number < text.Lines.Count);
                var boundaryStrip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, boundary.Tokens[0]));
                Assert.True(boundaryStrip.Focus(NavigationMethod.Directional));
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                PageScreenshots.Settle(window);
                var focusedStrip = Assert.IsType<WordStripToken>(window.FocusManager!.GetFocusedElement());
                Assert.Same(text.Lines[boundary.Number].Tokens[0], focusedStrip.Tag);
                AssertBounded(lines);
                var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => ReferenceEquals(viewer.Content, lines));
                reader.Offset = new Vector(0, Math.Max(0, reader.Extent.Height - reader.Viewport.Height));
                PageScreenshots.Settle(window);
                Assert.Contains(lines.GetRealizedContainers(),
                    control => ReferenceEquals(control.DataContext, text.Lines[^1]));
                AssertBounded(lines);

                await inText.OpenTokenCardAsync(text.Lines[0].Tokens[0]);
                PageScreenshots.Settle(window);
                var token = text.Lines[0].Tokens[0];
                var card = Assert.Single(panel.GetVisualDescendants().OfType<WordCard>(),
                    candidate => candidate.Key == token.PresentationKey && candidate.IsEffectivelyVisible);
                Assert.Same(token, Assert.Single(card.Document!.Sections.OfType<WordCardAnalysis>()).Token);
                Assert.Same(card, window.FocusManager!.GetFocusedElement());
                var origin = card.TranslatePoint(default, reader);
                Assert.NotNull(origin);
                Assert.True(new Rect(reader.Viewport).Intersects(new Rect(origin!.Value, card.Bounds.Size)));
                AssertBounded(lines);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void ALongSentencePagesItsStripsAndKeyboardNavigationCrossesThePageBoundary()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var texts = new TextWordsViewModel(client, selection);
            var assess = new AssessViewModel(client, selection);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client);
            var text = new ResultsTextViewModel(new TextLines(Guid.NewGuid(), "Long sentence",
                [new TextLine(1, Enumerable.Range(0, 500).Select(index =>
                    new TextToken($"word-{index}", $"word-{index}", null, null)).ToArray())]),
                new Dictionary<string, AssessmentWordResult>());
            inText.Texts.Add(text);
            inText.SelectedText = text;
            var panel = new ResultsInTextPanel(inText);
            var window = new Window { Content = panel, Width = 1240, Height = 780 };
            window.Show();
            try
            {
                PageScreenshots.Settle(window);
                var words = Assert.Single(panel.GetVisualDescendants().OfType<ProgressiveItemsControl>(),
                    control => ReferenceEquals(control.FullItemsSource, text.Lines[0].Tokens));
                Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
                var strip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, text.Lines[0].Tokens[19]));
                Assert.True(strip.Focus(NavigationMethod.Directional));
                window.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                window.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.None, null);
                PageScreenshots.Settle(window);
                Assert.Same(text.Lines[0].Tokens[20], Assert.IsType<WordStripToken>(window.FocusManager!.GetFocusedElement()).Tag);
                Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
                await inText.OpenTokenCardAsync(text.Lines[0].Tokens[^1]);
                PageScreenshots.Settle(window);
                var token = text.Lines[0].Tokens[^1];
                var card = Assert.Single(panel.GetVisualDescendants().OfType<WordCard>(),
                    candidate => candidate.Key == token.PresentationKey && candidate.IsEffectivelyVisible);
                Assert.Same(token, Assert.Single(card.Document!.Sections.OfType<WordCardAnalysis>()).Token);
                Assert.Same(card, window.FocusManager!.GetFocusedElement());
                Assert.Contains(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, text.Lines[0].Tokens[^1]));
                Assert.InRange(words.GetRealizedContainers().Count(), 1, 22);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void PageDownMovesByTheReaderViewportLines()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient();
            var selection = new SelectionViewModel(client);
            var texts = new TextWordsViewModel(client, selection);
            var assess = new AssessViewModel(client, selection);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { },
                new ChangesViewModel(client), client);
            var text = new ResultsTextViewModel(new TextLines(Guid.NewGuid(), "Long Text",
                Enumerable.Range(1, 64).Select(number => new TextLine(number,
                    [new TextToken($"word-{number}", $"word-{number}", null, null)])).ToArray()),
                new Dictionary<string, AssessmentWordResult>());
            inText.Texts.Add(text);
            inText.SelectedText = text;
            var panel = new ResultsInTextPanel(inText);
            var window = new Window { Content = panel, Width = 1040, Height = 780 };
            window.Show();
            try
            {
                PageScreenshots.Settle(window);
                var lines = Assert.Single(panel.GetVisualDescendants().OfType<ItemsControl>(),
                    control => ReferenceEquals(control.ItemsSource, inText.VisibleLines));
                var reader = Assert.Single(panel.GetVisualDescendants().OfType<ScrollViewer>(),
                    viewer => ReferenceEquals(viewer.Content, lines));
                var first = Assert.IsType<ResultsLineViewModel>(lines.ContainerFromIndex(0)?.DataContext);
                var localLinesPerPage = Math.Max(1,
                    (int)Math.Floor(reader.Viewport.Height / Math.Max(1, lines.ContainerFromIndex(0)!.Bounds.Height)));
                Assert.InRange(localLinesPerPage, 1, text.Lines.Count - 1);

                var firstStrip = Assert.Single(panel.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, first.Tokens[0]));
                Assert.True(firstStrip.Focus(NavigationMethod.Directional));
                Assert.Same(firstStrip, window.FocusManager!.GetFocusedElement());
                window.KeyPress(Key.PageDown, RawInputModifiers.None, PhysicalKey.None, null);
                PageScreenshots.Settle(window);

                var focused = Assert.IsType<WordStripToken>(window.FocusManager!.GetFocusedElement());
                Assert.Same(inText.VisibleLines[localLinesPerPage].Tokens[0], focused.Tag);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(30));
    }

    private static void AssertBounded(ItemsControl lines)
    {
        Assert.Equal(2000, lines.ItemCount);
        var realized = lines.GetRealizedContainers().Count();
        Assert.InRange(realized, 1, 31);
    }
}
