using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewHLayoutTests
{
    [Theory]
    [InlineData(640, 600, 150)]
    [InlineData(820, 600, 100)]
    [InlineData(1040, 780, 150)]
    public void ProgressHeadingRemainsVisibleAtNarrowWidthsAndEnlargedZoom(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var settings = SettingsPageTests.WindowSettings(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                workspace.TechDemoNotice?.GotItCommand.Execute(null);
                workspace.ShowPageCommand.Execute(WorkspacePage.Progress);
                LayoutInvariantFixtures.Apply(window, new LayoutCell(width, height, zoom));
                PageScreenshots.Settle(window);
                var page = window.PageView<ProgressPage>();
                var heading = Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                    AutomationProperties.GetAutomationId(text) == AutomationIds.ProgressHeading);
                Assert.True(heading.IsEffectivelyVisible);
                var bounds = new Rect(heading.Bounds.Size).TransformToAABB(heading.TransformToVisual(window)!.Value);
                Assert.True(bounds.Left >= 0 && bounds.Right <= window.ClientSize.Width + 1);
                Assert.True(bounds.Top >= 0 && bounds.Bottom <= window.ClientSize.Height + 1);
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                WindowZoomPolicy.Apply(Application.Current!, previousZoom);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotFact]
    public void CaptureProgressAtFourSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(seedReaderToken: false);
            var settings = SettingsPageTests.WindowSettings(window);
            var previousZoom = settings.ZoomPercent;
            try
            {
                workspace.ShowPageCommand.Execute(WorkspacePage.Progress);
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current.RequestedThemeVariant = variant;
                    foreach (var zoom in new[] { 100, 150 })
                    foreach (var (name, width, height) in new[]
                    {
                        ("thin", 820, 600), ("half", 1040, 780),
                        ("full", 1920, 1080), ("short", 1040, 480),
                    })
                    {
                        LayoutInvariantFixtures.Apply(window, new LayoutCell(width, height, zoom));
                        PageScreenshots.Settle(window);
                        PageScreenshots.Save(window, Path.Combine(folder, $"h-progress-{name}-{zoom}-{theme}.png"));
                    }
                }
            }
            finally
            {
                settings.ZoomPercent = previousZoom;
                WindowZoomPolicy.Apply(Application.Current!, previousZoom);
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
