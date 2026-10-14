using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsTooltipViewportTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FrameChangesCloseTransientTipsAndNativeHoverReopensInsideTheViewport(bool zoomChange, bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var gear = window.FindControl<Button>("SettingsButton")!;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = 1920;
                window.Height = 1080;
                var settings = workspace.PageModel<SettingsPageModel>().Settings!;
                settings.ZoomPercent = 100;
                window.Show();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, gear, "Settings");
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                await Hover(window, gear);
                Capture(window, zoomChange, dark, "before");
                if (zoomChange) settings.ZoomPercent = 150;
                else
                {
                    window.Width = 1040;
                    window.Height = 780;
                }
                PageScreenshots.Settle(window);
                Capture(window, zoomChange, dark, "changed");
                Assert.False(ToolTip.GetIsOpen(gear), "The transient gear tooltip retained its old frame position.");
                await Hover(window, gear);
                var tip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>(), item => item.IsEffectivelyVisible);
                var bounds = ClearTipPlacement.AreaOf(tip, window);
                Assert.True(new Rect(window.ClientSize).Inflate(1).Contains(bounds),
                    $"The reopened gear tooltip {bounds} leaves client {window.ClientSize}.");
                LayoutAssertions.AssertCurrent(window);
                Capture(window, zoomChange, dark, "reopened");
            }
            finally
            {
                ToolTip.SetIsOpen(gear, false);
                gear.ClearValue(ToolTip.ShowDelayProperty);
                workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = 100;
                Application.Current.RequestedThemeVariant = priorTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static async Task Hover(Window window, Control owner)
    {
        ToolTip.SetShowDelay(owner, 0);
        window.MouseMove(new Point(window.ClientSize.Width / 2, 100));
        PageScreenshots.Settle(window);
        var area = ClearTipPlacement.AreaOf(owner, window);
        window.MouseMove(area.Center);
        PageScreenshots.Settle(window);
        await Task.Yield();
        PageScreenshots.Settle(window);
        Assert.True(ToolTip.GetIsOpen(owner), "Native hover did not open the Settings gear tooltip.");
    }

    private static void Capture(Window window, bool zoom, bool dark, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var capture = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Tooltip frame did not render.");
        capture.Save(Path.Combine(folder, $"settings-tooltip-{(zoom ? "zoom" : "resize")}-{(dark ? "dark" : "light")}-{state}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
