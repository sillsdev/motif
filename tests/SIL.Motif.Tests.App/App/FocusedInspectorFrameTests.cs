using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class FocusedInspectorFrameTests
{
    [Theory]
    [InlineData(640, 100, false)]
    [InlineData(640, 100, true)]
    [InlineData(640, 150, false)]
    [InlineData(640, 150, true)]
    [InlineData(960, 150, false)]
    [InlineData(960, 150, true)]
    public void NarrowInspectorUsesWorkingAreaAndReturnsToTheSamePage(int width, int zoom, bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Width = width;
                window.Height = 600;
                workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = zoom;
                window.Show();
                PageScreenshots.Settle(window);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var working = host.FindControl<ContentControl>("WorkingPageHost")!;
                var region = Assert.IsType<Grid>(working.GetVisualParent());
                var content = working.Content;
                var page = workspace.CurrentPage;
                var strip = host.FindControl<Button>("InspectorStrip")!;
                HeadlessClick.Click(window, strip, "Show Inspector");
                PageScreenshots.Settle(window);
                Capture(window, width, zoom, dark);
                Assert.False(region.IsVisible);
                var inspector = host.FindControl<Border>("InspectorPane")!;
                Assert.True(inspector.IsEffectivelyVisible);
                var bounds = new Rect(inspector.Bounds.Size).TransformToAABB(inspector.TransformToVisual(window)!.Value);
                var rail = host.FindControl<ContentControl>("RailHost")!;
                var railBounds = new Rect(rail.Bounds.Size).TransformToAABB(rail.TransformToVisual(window)!.Value);
                Assert.InRange(bounds.Left, railBounds.Right - 1, railBounds.Right + 1);
                Assert.InRange(bounds.Right, window.ClientSize.Width - 1, window.ClientSize.Width + 1);
                var back = host.FindControl<Button>("InspectorHideButton")!;
                Assert.Equal("Back to page", back.Content);
                HeadlessClick.Click(window, back, "Back to page");
                PageScreenshots.Settle(window);
                Assert.True(region.IsEffectivelyVisible);
                Assert.Same(content, working.Content);
                Assert.Equal(page, workspace.CurrentPage);
                Assert.True(strip.IsFocused);
                Assert.False(inspector.IsVisible);
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClosingANarrowObjectRestoresThePreviousPaneChoice(bool previouslyOpen)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Width = 640;
                window.Height = 600;
                workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = 150;
                window.Show();
                PageScreenshots.Settle(window);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var working = host.FindControl<ContentControl>("WorkingPageHost")!;
                var region = Assert.IsType<Grid>(working.GetVisualParent());
                var content = working.Content;
                if (previouslyOpen)
                    HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                host.SetObjectInspectorOpen(true);
                host.ShowInspector();
                PageScreenshots.Settle(window);
                Assert.False(region.IsVisible);
                host.SetObjectInspectorOpen(false);
                PageScreenshots.Settle(window);
                Assert.Equal(previouslyOpen, host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.Equal(!previouslyOpen, region.IsVisible);
                Assert.Same(content, working.Content);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void Capture(Window window, int width, int zoom, bool dark)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Inspector frame did not render.");
        frame.Save(Path.Combine(folder, $"focused-inspector-{width}-600-{zoom}-{(dark ? "dark" : "light")}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
