using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Assess;
using Xunit;
using Group = SIL.Motif.App.ViewModels.SettingsGroup;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsPageCaptureTests
{
    [ScreenshotFact]
    public void PageAtFourSizesBothThemesAndZooms()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, frame) = await PageScreenshots.OpenOverSampleData(parse: false,
                configure: (client, _) => client.DefaultSelectionIs(new NamedSelectionProjection("Default",
                    [Guid.Parse("11111111-0000-0000-0000-000000000001")], [], string.Empty, string.Empty,
                    new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), "1")));
            var overview = workspace.PageModel<OverviewPageModel>();
            overview.Overview = overview.Overview! with
            {
                WritingSystems =
                [
                    new WritingSystemDisplay("sw", "Swahili", "SW", WritingSystemKind.Vernacular, 0, true,
                        "Charis SIL", "", false, new Dictionary<string, double> { ["Normal"] = 12 })
                    { StyleFonts = new Dictionary<string, WritingSystemStyleFont> { ["Normal"] = new("Charis SIL", "") } },
                    new WritingSystemDisplay("en", "English", "EN", WritingSystemKind.Analysis, 0, true,
                        "Andika", "", false, new Dictionary<string, double> { ["Normal"] = 11 })
                    { StyleFonts = new Dictionary<string, WritingSystemStyleFont> { ["Normal"] = new("Andika", "") } },
                ],
            };
            var settings = SettingsTestAccess.Model(frame);
            var page = SettingsTestAccess.Open(frame);
            var window = frame;
            try
            {
                window.Show();
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
                    foreach (var group in new[] { Group.Display, Group.KeyboardShortcuts, Group.Parsing, Group.ProposalChecks, Group.WritingSystems, Group.Texts, Group.HelpAndReports, Group.About })
                    {
                        SettingsTestAccess.Select(page, group);
                        PageScreenshots.Settle(window);
                        var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                        Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
                        using var capture = window.CaptureRenderedFrame() ??
                            throw new InvalidOperationException("The Settings page did not render.");
                        capture.Save(Path.Combine(folder, $"page-{size}-{theme.Key}-{zoom}-{group}.png"),
                            Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }
}
