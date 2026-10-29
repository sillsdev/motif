using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Assess;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsPageMatrixScreenshots
{
    [ScreenshotFact]
    public void RegisteredPageAtFourSizesBothThemesAndZooms()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var settingsPath = SettingsViewTests.NewSettingsPath();
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")),
                settingsFilePath: settingsPath);
            try
            {
                var page = SettingsTestAccess.Open(window);
                var settings = SettingsTestAccess.Model(window);
                foreach (var (size, width, height) in new[]
                         { ("full", 1920, 1080), ("half", 1040, 780), ("thin", 640, 900), ("short", 1040, 600) })
                foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
                foreach (var zoom in new[] { 100, 150 })
                {
                    window.Width = width;
                    window.Height = height;
                    settings.ThemeChoice = theme == ThemeVariant.Dark ? WindowThemeChoice.Dark : WindowThemeChoice.Light;
                    settings.ZoomPercent = zoom;
                    PageScreenshots.Settle(window);
                    Assert.Equal(width, window.ClientSize.Width);
                    Assert.Equal(height, window.ClientSize.Height);
                    Assert.True(window.MinHeight >= 600);
                    foreach (var (group, slug) in SettingsViewTests.GroupStates)
                    {
                        SettingsTestAccess.Select(page, group);
                        PageScreenshots.Settle(window);
                        var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1,
                            $"{size}/{theme.Key}/{zoom}/{slug}: Settings scrolls horizontally.");
                        SettingsTestAccess.AssertReachable(window, page.FindControl<Button>("SettingsClose")!);
                        using var capture = window.CaptureRenderedFrame() ??
                            throw new InvalidOperationException("The registered Settings page did not render.");
                        capture.Save(Path.Combine(folder, $"settings-page-{slug}-{size}-{zoom}-{(theme.Key?.ToString() ?? throw new InvalidOperationException("Theme key is missing.")).ToLowerInvariant()}.png"),
                            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                    var themeName = theme == ThemeVariant.Dark ? "dark" : "light";
                    foreach (var (scene, route) in new[]
                             { ("1-overview", WorkspacePage.Overview), ("3-try-a-word", WorkspacePage.TryAWord) })
                    {
                        workspace.CurrentPage = route;
                        if (route == WorkspacePage.TryAWord) await PageScreenshots.TryTheSampleWord(workspace);
                        PageScreenshots.Settle(window);
                        using var capture = window.CaptureRenderedFrame() ??
                            throw new InvalidOperationException($"The {scene} frame did not render.");
                        capture.Save(Path.Combine(folder, $"{scene}-{size}-{zoom}-{themeName}.png"),
                            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                    page = SettingsTestAccess.Open(window);
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
                SettingsViewTests.DeleteSettingsPath(settingsPath);
            }
        }, TimeSpan.FromMinutes(3));
    }
}
