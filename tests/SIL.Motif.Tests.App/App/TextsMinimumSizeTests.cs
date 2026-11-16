using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>At the 640 by 600 minimum the Texts reader still shows the words of the text a person chose.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsMinimumSizeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void TheReaderShowsReadableWordsAtTheMinimumWindowSize(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(width: 640);
            var previousZoom = (int)Math.Round(WindowZoomPolicy.Transform.ScaleX * 100);
            try
            {
                WindowZoomPolicy.Apply(Application.Current!, zoom);
                window.Height = 600;
                workspace.PageModel<TextsPageModel>().ResultsInText.CloseTokenCard();
                await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, window);
                Capture(window, zoom);
                var panel = AnalyzeTextsLayoutTests.Panel(window);
                var client = new Rect(window.ClientSize);
                var strips = AnalyzeTextsLayoutTests.Strips(panel).ToArray();
                output.WriteLine($"client {client}; panel {AnalyzeTextsLayoutTests.BoundsIn(panel, window)}; strips {strips.Length}");
                foreach (var strip in strips.Take(6))
                    output.WriteLine($"  strip {AnalyzeTextsLayoutTests.BoundsIn(strip, window)}");
                var scroller = strips.First().GetVisualAncestors().OfType<ScrollViewer>().First();
                var viewport = AnalyzeTextsLayoutTests.BoundsIn(scroller, window).Intersect(client);
                var readable = strips.Select(strip => AnalyzeTextsLayoutTests.BoundsIn(strip, window).Intersect(viewport))
                    .Where(seen => seen.Height >= (zoom == 100 ? 80 : 40) && seen.Width >= 40).ToArray();
                Assert.True(readable.Length > 0,
                    $"No word shows enough of itself at the minimum size (viewport {viewport}, {strips.Length} words laid out).");
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, previousZoom);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [Fact]
    public void SourcesCheckBoxesShowTheChosenTextsWithoutAnotherRedraw()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await AnalyzeTextsLayoutTests.OpenAnalyzeTexts(width: 1240);
            try
            {
                var texts = workspace.Selection.Texts.ToArray();
                Assert.NotEmpty(texts);
                foreach (var text in texts) text.IsChecked = false;
                PageScreenshots.Settle(window);
                texts[0].IsChecked = true;
                PageScreenshots.Settle(window);
                var list = window.GetVisualDescendants().OfType<ListBox>().Single(box => box.Name == "SourcesPanelTextsItems");
                var boxes = list.GetVisualDescendants().OfType<CheckBox>().ToArray();
                Assert.NotEmpty(boxes);
                Assert.Equal(texts.Count(text => text.IsChecked), boxes.Count(box => box.IsChecked == true));
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(3));
    }

    private static void Capture(Window window, int zoom)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Texts did not render.");
        frame.Save(Path.Combine(folder, $"texts-min-640-600-{zoom}.png"), PngBitmapEncoderOptions.Default);
    }
}
