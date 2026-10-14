using SIL.Motif.App;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HFrameScreenshots
{
    public static TheoryData<string, int, int, int> Sizes
    {
        get
        {
            var sizes = new TheoryData<string, int, int, int>();
            foreach (var theme in new[] { "light", "dark" })
            foreach (var size in new[] { (1920, 1080), (960, 1040), (640, 600), (1600, 600) })
            foreach (var zoom in new[] { 100, 150 })
                sizes.Add(theme, size.Item1, size.Item2, zoom);
            return sizes;
        }
    }

    [ScreenshotTheory]
    [MemberData(nameof(Sizes))]
    public void CaptureFramePages(string theme, int width, int height, int zoom)
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current!.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = width;
                window.Height = height;
                SettingsTestAccess.Model(window).ZoomPercent = zoom;
                foreach (var page in new[] { WorkspacePage.Overview, WorkspacePage.Texts, WorkspacePage.Review })
                {
                    workspace.CurrentPage = page;
                    if (page == WorkspacePage.Texts) workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                    PageScreenshots.Save(window, Path.Combine(folder, $"frame-{page}-{width}-{height}-{zoom}-{theme}.png"));
                }
                var settings = window.FindControl<Button>("SettingsButton")!;
                PageScreenshots.Settle(window);
                var point = settings.TranslatePoint(new Point(settings.Bounds.Width / 2, settings.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                Assert.True(SettingsTestAccess.Page(window).IsEffectivelyVisible);
                PageScreenshots.Save(window, Path.Combine(folder, $"frame-Settings-{width}-{height}-{zoom}-{theme}.png"));
                HeadlessClick.Click(window, SettingsTestAccess.Page(window).FindControl<Button>("SettingsClose")!, "Leave Settings");
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = previousTheme;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
