using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TimingTitleWrappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimingScopeKeepsItsFullHeadingInsideTheNarrowPage(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            var theme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window).ZoomPercent = 150;
                workspace.CurrentPage = WorkspacePage.Timing;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<TimingPageModel>();
                var heading = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == model.ScopeLabel);
                if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is { } folder)
                {
                    Directory.CreateDirectory(folder);
                    using var frame = window.CaptureRenderedFrame()!;
                    frame.Save(Path.Combine(folder, $"timing-heading-640-600-150-{(dark ? "dark" : "light")}.png"),
                        PngBitmapEncoderOptions.Default);
                }
                Assert.True(heading.TextLayout.TextLines.Count > 1, "The complete scope heading needs another line.");
                Assert.All(heading.TextLayout.TextLines, line => Assert.True(
                    line.WidthIncludingTrailingWhitespace <= heading.Bounds.Width + 0.5,
                    $"Heading line needs {line.WidthIncludingTrailingWhitespace}, available {heading.Bounds.Width}."));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = theme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
