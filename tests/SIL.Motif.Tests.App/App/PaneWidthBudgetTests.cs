using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PaneWidthBudgetTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(800, 100)]
    [InlineData(800, 150)]
    [InlineData(960, 100)]
    [InlineData(960, 150)]
    [InlineData(1000, 100)]
    [InlineData(1000, 150)]
    public void ExplicitReopeningReservesWorkingSpaceBeforeAdmittingBothPanes(int width, int zoom)
    {
        avalonia.Invoke(() =>
        {
            var scale = zoom / 100d;
            var page = new Border();
            var host = new WorkspaceLayoutHost { Page = page, Rail = new Border { Width = 44 } };
            var window = new MotifTestWindow { Width = width, Height = 600, Content = new LayoutTransformControl
                { LayoutTransform = new ScaleTransform(scale, scale), Child = host } };
            try
            {
                window.Show();
                host.SetPagePanels(new PaneProvider());
                host.UpdateViewport(width / scale);
                var side = host.FindControl<Border>("SidePanelBorder")!;
                var inspector = host.FindControl<Border>("InspectorPane")!;
                host.ToggleSidePanel();
                if (!inspector.IsVisible) Click(window, host.FindControl<Button>("InspectorStrip")!);
                PageScreenshots.Settle(window);
                Assert.True(inspector.IsVisible);
                AssertPairBudget(side, inspector, page, width, zoom);
                AssertInside(window, host.FindControl<Button>("InspectorHideButton")!);

                if (side.IsVisible) host.ToggleSidePanel();
                host.ToggleSidePanel();
                PageScreenshots.Settle(window);
                Assert.True(side.IsVisible);
                AssertPairBudget(side, inspector, page, width, zoom);
                AssertInside(window, host.FindControl<Button>("SidePanelCollapseButton")!);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertPairBudget(Border side, Border inspector, Control page, int width, int zoom)
    {
        var pairFits = width / (zoom / 100d) >= 44 + 248 + 360 + 320;
        Assert.Equal(pairFits, side.IsVisible && inspector.IsVisible);
        if (side.IsVisible && inspector.IsVisible)
            Assert.True(page.Bounds.Width >= 320,
                $"{width}/{zoom}: both panes leave only {page.Bounds.Width} DIP for the working page.");
    }

    private static void Click(Window window, Control control)
    {
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void AssertInside(Window window, Control control)
    {
        var first = control.TranslatePoint(default, window)!.Value;
        var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(new Rect(window.ClientSize).Contains(new Rect(first, last)));
    }

    private sealed class PaneProvider : IWorkspacePaneProvider
    {
        public Control SidePanel { get; } = new Border();
        public Control InspectorContent { get; } = new Border();
    }
}
