using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsFrameRoutingTests
{
    [Theory]
    [InlineData(1240, 600, 100, false)]
    [InlineData(640, 600, 150, false)]
    [InlineData(640, 900, 150, false)]
    [InlineData(1240, 600, 100, true)]
    [InlineData(640, 600, 150, true)]
    [InlineData(640, 900, 150, true)]
    public void GearAndShortcutOpenTheRegisteredPageAndBackReturns(int width, int height, int zoom, bool keyboard)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Width = width;
                window.Height = height;
                var settings = workspace.PageModel<SettingsPageModel>().Settings!;
                settings.ZoomPercent = zoom;
                window.Show();
                workspace.ShowPageCommand.Execute(WorkspacePage.Timing);
                PageScreenshots.Settle(window);
                if (keyboard)
                {
                    // Settings is Command+, on macOS and Control+, elsewhere; the shortcut catalog resolves it per platform.
                    var chord = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
                    window.KeyPress(Key.OemComma, chord, PhysicalKey.Comma, null);
                    window.KeyRelease(Key.OemComma, chord, PhysicalKey.Comma, null);
                }
                else FakeComposedWindow.Click(window, "Settings");
                PageScreenshots.Settle(window);
                Capture(window, $"settings-entry-{keyboard}-{width}-{height}-{zoom}");
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                var page = window.PageView<SettingsPage>();
                Assert.True(page.IsEffectivelyVisible);
                Assert.Same(settings, page.DataContext);
                Assert.Same(settings, page.Navigation.DataContext);
                Assert.Null(window.FindControl<Button>("SettingsButton")!.Flyout);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.Same(page.Navigation, host.FindControl<ContentControl>("SidePanelContent")!.Content);
                FakeComposedWindow.Click(window, "Settings");
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                Assert.True(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                if (width == 640)
                {
                    HeadlessClick.Click(window, host.FindControl<Button>("SidePanelCollapseButton")!, "Collapse Settings sections");
                    PageScreenshots.Settle(window);
                }
                var back = page.FindControl<Button>("SettingsClose")!;
                back.BringIntoView();
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, back, "Back from Settings");
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Timing, workspace.CurrentPage);
                Assert.True(window.FindControl<Button>("SettingsButton")!.IsFocused);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
    private static void Capture(MainWindow window, string name)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Settings frame did not render.");
        frame.Save(Path.Combine(folder, name + ".png"), PngBitmapEncoderOptions.Default);
        if (window.FindControl<Button>("SettingsButton")?.Flyout is Avalonia.Controls.Flyout { IsOpen: true, Content: Control content } &&
            TopLevel.GetTopLevel(content) is { } popup)
        {
            using var popupFrame = popup.CaptureRenderedFrame() ?? throw new InvalidOperationException("Settings popup did not render.");
            popupFrame.Save(Path.Combine(folder, name + "-popup.png"), PngBitmapEncoderOptions.Default);
        }
    }

}
