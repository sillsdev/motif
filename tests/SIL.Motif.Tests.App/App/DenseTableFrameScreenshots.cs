using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class DenseTableFrameScreenshots
{
    [ScreenshotFact]
    public void DiagnosticsWarningsFitInTheActualFrameAtEveryViewport()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        Directory.CreateDirectory(folder);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var failures = new List<string>();
            try
            {
                workspace.Context.Diagnostics = true;
                workspace.Context.OpenDiagnostics(DiagnosticsTab.Warnings);
                var page = workspace.PageModel<DiagnosticsPageModel>();
                await Wait.UntilAsync(() => page.Warnings.Rows.Count > 0);
                PageScreenshots.Settle(window);
                var settings = SettingsTestAccess.Model(window);
                var list = Assert.Single(window.GetVisualDescendants().OfType<ListBox>(),
                    control => control.Name == "DiagnosticWarningRows");
                var selected = page.Warnings.Rows[0];
                foreach (var (themeName, theme) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (name, width, height) in new[]
                         { ("thin", 640, 600), ("half", 960, 780), ("full", 1920, 1080), ("short", 960, 600) })
                {
                    Application.Current.RequestedThemeVariant = theme;
                    settings.ZoomPercent = zoom;
                    window.Width = width;
                    window.Height = height;
                    list.ScrollIntoView(selected);
                    PageScreenshots.Settle(window);
                    var state = $"warnings-frame-{name}-{width}x{height}-{themeName}-{zoom}";
                    using var frame = window.CaptureRenderedFrame() ??
                        throw new InvalidOperationException($"No frame rendered for {state}.");
                    frame.Save(Path.Combine(folder, state + ".png"), PngBitmapEncoderOptions.Default);
                    try
                    {
                        Assert.Equal(width, window.ClientSize.Width);
                        Assert.Equal(height, window.ClientSize.Height);
                        var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(selected));
                        Assert.True(row.IsEffectivelyVisible);
                        Assert.True(row.Bounds.Width > 0 && row.Bounds.Height > 0);
                        LayoutAssertions.AssertCurrent(window);
                    }
                    catch (Exception failure)
                    {
                        failures.Add($"{state}: {failure.Message}");
                    }
                }
                File.WriteAllLines(Path.Combine(folder, "warnings-frame-failures.txt"), failures);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                WindowZoomPolicy.Apply(Application.Current, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(4));
    }
}
