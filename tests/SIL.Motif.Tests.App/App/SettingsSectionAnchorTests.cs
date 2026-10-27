using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsSectionAnchorTests
{
    [Theory]
    [InlineData(600, false)]
    [InlineData(600, true)]
    [InlineData(900, false)]
    [InlineData(900, true)]
    public void NativeSectionSelectionSurvivesNavigatorCollapseAndResizingAfterManualScroll(int height, bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = 640;
                window.Height = height;
                SettingsTestAccess.Model(window).ZoomPercent = 150;
                window.Show();
                var page = SettingsTestAccess.Open(window);
                HeadlessClick.Click(window, window.FindInWindowOrWorkspace<Button>("SettingsButton")!, "Settings");
                PageScreenshots.Settle(window);
                var groups = page.Navigation.FindControl<ListBox>("SettingsGroups")!;
                var index = SettingsTestAccess.Model(window).PageGroupOptions.ToList()
                    .FindIndex(option => option.Group == SettingsGroup.KeyboardShortcuts);
                var item = Assert.IsType<ListBoxItem>(groups.ContainerFromIndex(index));
                HeadlessClick.Click(window, item, "Keyboard shortcuts");
                PageScreenshots.Settle(window);
                Assert.Equal(SettingsGroup.KeyboardShortcuts, SettingsTestAccess.Model(window).SelectedGroup);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                HeadlessClick.Click(window, host.FindControl<Button>("SidePanelCollapseButton")!, "Collapse side bar");
                PageScreenshots.Settle(window);
                Capture(window, height, dark, "selected");
                var section = page.FindControl<StackPanel>("SettingsKeyboardShortcutsSection")!;
                var heading = Assert.IsAssignableFrom<TextBlock>(section.Children[0]);
                var search = page.FindControl<TextBox>("ShortcutSearchBox")!;
                var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                AssertInside(window, scroll, heading);
                AssertInside(window, scroll, search);
                var selectedOffset = scroll.Offset.Y;
                var center = new Rect(scroll.Bounds.Size).TransformToAABB(scroll.TransformToVisual(window)!.Value).Center;
                window.MouseMove(center);
                window.MouseWheel(center, new Vector(0, -3), RawInputModifiers.None);
                PageScreenshots.Settle(window);
                Assert.True(scroll.Offset.Y > selectedOffset, "Native wheel input did not scroll inside the section.");
                var manualOffset = scroll.Offset.Y;
                window.Width = 700;
                PageScreenshots.Settle(window);
                Assert.InRange(Math.Abs(scroll.Offset.Y - manualOffset), 0, 1);
                Capture(window, height, dark, "manual-scroll");
            }
            finally
            {
                SettingsTestAccess.Model(window).ZoomPercent = 100;
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void AssertInside(Window window, ScrollViewer viewport, Control control)
    {
        var area = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(window)!.Value);
        Assert.True(control.IsEffectivelyVisible && new Rect(window.ClientSize).Inflate(1).Contains(area),
            $"{control.Name}/{(control as TextBlock)?.Text} at {area} leaves client {window.ClientSize}.");
        var inside = new Rect(control.Bounds.Size).TransformToAABB(control.TransformToVisual(viewport)!.Value);
        Assert.True(inside.Top >= -1 && inside.Bottom <= viewport.Viewport.Height + 1 &&
            inside.Left >= -1 && inside.Right <= viewport.Viewport.Width + 1,
            $"{control.Name}/{(control as TextBlock)?.Text} at {inside} leaves Settings viewport {viewport.Viewport}.");
    }

    private static void Capture(Window window, int height, bool dark, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Settings did not render.");
        frame.Save(Path.Combine(folder, $"settings-anchor-{(int)Math.Round(window.ClientSize.Width)}-{height}-150-{(dark ? "dark" : "light")}-{state}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
