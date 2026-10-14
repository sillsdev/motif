using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsHeaderResponsiveScreenshots
{
    public static TheoryData<string, int, int, int, string> Captures
    {
        get
        {
            var captures = new TheoryData<string, int, int, int, string>();
            foreach (var (size, width, height) in new[]
                { ("full", 1920, 1080), ("half", 960, 1040), ("thin", 640, 600), ("short", 1600, 600) })
            foreach (var zoom in new[] { 100, 150 })
            foreach (var theme in new[] { "light", "dark" })
                captures.Add(size, width, height, zoom, theme);
            return captures;
        }
    }

    [ScreenshotTheory]
    [MemberData(nameof(Captures))]
    public void CaptureTextsHeaderAtResponsiveSizes(string size, int width, int height, int zoom, string theme)
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
                var settings = SettingsTestAccess.Model(window);
                settings.ZoomPercent = zoom;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Save(window,
                    Path.Combine(folder, $"texts-{size}-{width}-{height}-{zoom}-{theme}.png"));
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = previousTheme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
