using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ChromeScreenshots
{
    [ScreenshotFact]
    public void CapturesTheChromeAtThreeWidthsAndTheGearAtBothZoomLevels()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                var topBar = Assert.IsType<Border>(window.FindInWindowOrWorkspace<Border>("TopBar"));
                topBar.Classes.Set("windowsChrome", true);
                foreach (var width in new[] { 820d, 1040d, 1240d })
                {
                    window.Width = width;
                    window.Height = 600;
                    PageScreenshots.Save(window, Path.Combine(folder, $"chrome-titlebar-{width}.png"));
                }

                var popup = SettingsViewTests.OpenSettings(window);
                var settings = Assert.IsType<SettingsViewModel>(popup.DataContext);
                var gear = Assert.IsType<Button>(window.FindInWindowOrWorkspace<Button>("SettingsButton"));
                foreach (var zoom in new[] { 100, 150 })
                {
                    settings.ZoomPercent = zoom;
                    PageScreenshots.Settle(window);
                    SaveGear(gear, folder, zoom / 100d * 3);
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [ScreenshotFact]
    public void CapturesAdvancedAiAtMinimumWindowSize()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(
                parse: false, advancedAiModeEnabled: true);
            try
            {
                window.Width = 820;
                window.Height = 600;
                workspace.CurrentPage = WorkspacePage.AiAssistants;
                PageScreenshots.Settle(window);
                var toggle = Assert.Single(window.GetVisualDescendants().OfType<CheckBox>(),
                    checkBox => checkBox.Content as string == "Turn on Advanced AI mode");
                toggle.BringIntoView();
                PageScreenshots.Settle(window);
                PageScreenshots.Save(window, Path.Combine(folder, "advanced-ai-minimum-size.png"));
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void SaveGear(Button gear, string folder, double scale)
    {
        var size = gear.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(
            new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
            new Vector(96 * scale, 96 * scale));
        bitmap.Render(gear);
        bitmap.Save(Path.Combine(folder, $"settings-gear-{(scale < 4 ? "100" : "150")}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
