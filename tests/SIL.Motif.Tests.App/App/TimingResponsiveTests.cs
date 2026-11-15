using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TimingResponsiveTests
{
    [ScreenshotFact]
    public void RecordedRuleRowsRemainReachableAtCompactSizes()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            var settings = SettingsTestAccess.Model(window);
            try
            {
                workspace.Context.Diagnostics = true;
                workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
                await workspace.Context.EvidencePublication;
                var page = window.PageView<DiagnosticsPage>();
                foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                {
                    Application.Current!.RequestedThemeVariant = variant;
                    foreach (var height in new[] { 600, 900 })
                    foreach (var zoom in new[] { 100, 150 })
                    {
                        window.Width = 640;
                        window.Height = height;
                        settings.ZoomPercent = zoom;
                        PageScreenshots.Settle(window);
                        var rows = page.FindControl<ListBox>("DiagnosticRuleRows")!;
                        Assert.True(rows.ItemCount > 0);
                        Assert.True(rows.IsEffectivelyVisible);
                        Assert.True(rows.Bounds.Width > 0 && rows.Bounds.Width <= page.Bounds.Width);
                        rows.ScrollIntoView(rows.ItemCount - 1);
                        PageScreenshots.Settle(window);
                        Assert.NotNull(rows.ContainerFromIndex(rows.ItemCount - 1));
                        LayoutAssertions.AssertCurrent(page);
                        using var frame = window.CaptureRenderedFrame()
                            ?? throw new InvalidOperationException("No rendered Diagnostics frame was available.");
                        frame.Save(Path.Combine(folder, $"diagnostics-640-{height}-{zoom}-{theme}.png"),
                            PngBitmapEncoderOptions.Default);
                    }
                }
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }
}
