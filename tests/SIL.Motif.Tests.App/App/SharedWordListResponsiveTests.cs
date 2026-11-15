using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SharedWordListResponsiveTests
{
    [Theory]
    [InlineData(640, 600, 150)]
    [InlineData(1040, 780, 100)]
    public void WordsStayReachableAtSupportedWindowSizes(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var settings = SettingsTestAccess.Model(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                window.Width = width;
                window.Height = height;
                settings.ZoomPercent = zoom;
                workspace.CurrentPage = WorkspacePage.Words;
                var words = workspace.PageModel<WordsPageModel>().Words;
                await Wait.UntilAsync(() => words.Rows.Count > 0, "The Selection words did not load.",
                    between: () => PageScreenshots.Settle(window));
                PageScreenshots.Settle(window);
                var panel = Assert.Single(window.GetVisualDescendants().OfType<TextWordsPanel>(),
                    control => control.IsEffectivelyVisible);
                var rows = panel.FindControl<ListBox>("TextWordsPanelRowsItems")!;
                Assert.True(rows.ItemCount > 0);
                Assert.True(rows.Bounds.Width > 0 && rows.Bounds.Width <= panel.Bounds.Width);
                rows.ScrollIntoView(rows.ItemCount - 1);
                PageScreenshots.Settle(window);
                Assert.NotNull(rows.ContainerFromIndex(rows.ItemCount - 1));
                LayoutAssertions.AssertCurrent(panel);
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotFact]
    public void CaptureWordsAcrossSizesZoomAndThemes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var settings = SettingsTestAccess.Model(window);
            var previousZoom = settings.ZoomPercent;
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                workspace.CurrentPage = WorkspacePage.Words;
                foreach (var (name, theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                foreach (var height in new[] { 600, 900 })
                foreach (var zoom in new[] { 100, 150 })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    window.Width = 640;
                    window.Height = height;
                    settings.ZoomPercent = zoom;
                    PageScreenshots.Settle(window);
                    var panel = Assert.Single(window.GetVisualDescendants().OfType<TextWordsPanel>(),
                        control => control.IsEffectivelyVisible);
                    LayoutAssertions.AssertCurrent(panel);
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No Words frame was available.");
                    frame.Save(Path.Combine(folder, $"words-640-{height}-{zoom}-{name}.png"),
                        PngBitmapEncoderOptions.Default);
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                settings.ZoomPercent = previousZoom;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }
}
