using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HFrameActionTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(1040)]
    [InlineData(1240)]
    public void ProgressStatusKeepsItsCompactTrackAndCanOpenWhileItsTooltipIsVisible(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            var finish = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var progress = workspace.Assess.Trace.ParseProgress;
            var running = progress.TrackAsync(() => finish.Task);
            try
            {
                window.Width = width;
                window.Height = 780;
                progress.Report(new AssessmentProgress(AssessmentStage.Parsing, 1, 3, "Parsing"));
                PageScreenshots.Settle(window);
                var status = window.FindControl<Button>("StatusParseProgress")!;
                Assert.True(status.IsEffectivelyVisible);
                var track = Assert.Single(status.GetVisualDescendants().OfType<ProgressBar>());
                Assert.Equal(60, track.Bounds.Width);
                Assert.Equal(4, track.Bounds.Height);
                ToolTip.SetShowDelay(status, 0);
                var point = status.TranslatePoint(new Point(status.Bounds.Width / 2, status.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                await Task.Yield();
                PageScreenshots.Settle(window);
                Assert.True(ToolTip.GetIsOpen(status));
                var tip = Assert.Single(window.GetVisualDescendants().OfType<ToolTip>(), control => control.IsEffectivelyVisible);
                var statusArea = new Rect(status.TranslatePoint(default, window)!.Value, status.Bounds.Size);
                var tipArea = new Rect(tip.TranslatePoint(default, window)!.Value, tip.Bounds.Size);
                Assert.False(statusArea.Intersects(tipArea), $"Progress tooltip {tipArea} covers its status control {statusArea}.");
                HeadlessClick.Click(window, status, "parse progress status");
                Assert.True(status.Flyout!.IsOpen);
            }
            finally
            {
                finish.TrySetResult(true);
                await running;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void StatusCountsOpenTheirPagesAtTheActualMinimumWidth(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window)
                    .ZoomPercent = zoom;
                workspace.PageOf(WorkspacePage.Review).Badge = "3";
                workspace.PageOf(WorkspacePage.Warnings).Badge = "2";
                PageScreenshots.Settle(window);
                foreach (var action in window.FindControl<Border>("TopBar")!.GetVisualDescendants()
                    .OfType<Button>().Where(button => button.IsEffectivelyVisible))
                    AssertInside(window, action);
                foreach (var (name, page) in new[]
                    { ("StatusChanges", WorkspacePage.Review), ("StatusWarnings", WorkspacePage.Warnings) })
                {
                    workspace.CurrentPage = WorkspacePage.Overview;
                    PageScreenshots.Settle(window);
                    var button = window.FindControl<Button>(name)!;
                    AssertInside(window, button);
                    Click(window, button);
                    PageScreenshots.Settle(window);
                    Assert.Equal(page, workspace.CurrentPage);
                }
                Assert.Equal(640, window.ClientSize.Width);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void DisabledPinnedActionAndItsReasonStayVisibleWhenInspectorCollapses(int zoom)
    {
        avalonia.Invoke(() =>
        {
            var context = new object();
            var apply = new Button { Content = "Apply", IsEnabled = false };
            var reason = new CopyableTextBlock { Text = "No changes selected" };
            var footer = new StackPanel { DataContext = context, Children = { apply, reason } };
            var host = new WorkspaceLayoutHost { Page = new Border(), Rail = new Border { Width = 44 } };
            var window = new Window { Width = 640, Height = 600, Content = new LayoutTransformControl
                { LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d), Child = host } };
            try
            {
                window.Show();
                host.SetPagePanels(new PaneProvider(footer));
                host.UpdateViewport(window.ClientSize.Width / (zoom / 100d));
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                Assert.Same(context, apply.DataContext);
                Assert.Same(context, reason.DataContext);
                Assert.False(apply.IsEffectivelyEnabled);
                AssertInside(window, apply);
                AssertInside(window, reason);
                Assert.Equal("No changes selected", reason.Text);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void ReopeningBothPanelsAtMinimumWidthKeepsTheirCloseControlsReachable(int zoom)
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border(), Rail = new Border { Width = 44 } };
            var window = new Window { Width = 640, Height = 600, Content = new LayoutTransformControl
                { LayoutTransform = new ScaleTransform(zoom / 100d, zoom / 100d), Child = host } };
            try
            {
                window.Show();
                host.SetPagePanels(new PaneProvider(new StackPanel(), new Border()));
                host.UpdateViewport(window.ClientSize.Width / (zoom / 100d));
                host.ToggleSidePanel();
                Click(window, host.FindControl<Button>("InspectorStrip")!);
                PageScreenshots.Settle(window);
                AssertInside(window, host.FindControl<Button>("InspectorHideButton")!);
                host.ToggleSidePanel();
                PageScreenshots.Settle(window);
                AssertInside(window, host.FindControl<Button>("SidePanelCollapseButton")!);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void NarrowingAnOpenPairKeepsInspectorReachableAndWideningRestoresSidePanel(int zoom)
    {
        avalonia.Invoke(() =>
        {
            var scale = zoom / 100d;
            var host = new WorkspaceLayoutHost { Page = new Border(), Rail = new Border { Width = 44 } };
            var window = new Window { Width = 980 * scale, Height = 600, Content = new LayoutTransformControl
                { LayoutTransform = new ScaleTransform(scale, scale), Child = host } };
            try
            {
                window.Show();
                host.SetPagePanels(new PaneProvider(new StackPanel(), new Border()));
                host.UpdateViewport(980);
                host.ToggleSidePanel();
                Click(window, host.FindControl<Button>("InspectorStrip")!);
                PageScreenshots.Settle(window);
                Assert.True(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                Assert.True(host.FindControl<Border>("InspectorPane")!.IsVisible);
                window.Width = 640;
                host.UpdateViewport(640 / scale);
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                AssertInside(window, host.FindControl<Button>("InspectorHideButton")!);
                window.Width = 980 * scale;
                host.UpdateViewport(980);
                PageScreenshots.Settle(window);
                AssertInside(window, host.FindControl<Button>("SidePanelCollapseButton")!);
                AssertInside(window, host.FindControl<Button>("InspectorHideButton")!);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void NarrowFreshnessStartsWithTheDateAndWideFreshnessKeepsItsLabel(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData();
            try
            {
                SettingsTestAccess.Model(window)
                    .ZoomPercent = zoom;
                window.Width = 1920;
                PageScreenshots.Settle(window);
                var full = workspace.FreshnessDetail;
                Assert.StartsWith("Baseline of ", full);
                window.Width = 640;
                PageScreenshots.Settle(window);
                Assert.Equal(full["Baseline of ".Length..], workspace.FreshnessDetail);
                Assert.Contains(window.FindControl<Border>("TopBar")!.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    text => text.Text == workspace.FreshnessDetail);
                window.Width = 1920;
                PageScreenshots.Settle(window);
                Assert.Equal(full, workspace.FreshnessDetail);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(WorkspacePage.Overview, 100)]
    [InlineData(WorkspacePage.Overview, 150)]
    [InlineData(WorkspacePage.Texts, 100)]
    [InlineData(WorkspacePage.Texts, 150)]
    public void ModifiedSpaceCannotEraseTheCurrentRailLocation(WorkspacePage page, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window)
                    .ZoomPercent = zoom;
                workspace.CurrentPage = page;
                PageScreenshots.Settle(window);
                var rail = window.FindControl<ListBox>("PageList")!;
                var current = workspace.PageOf(page);
                Assert.Equal(page, workspace.CurrentPage);
                Assert.Same(current, rail.SelectedItem);
                var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(control =>
                    ReferenceEquals(control.DataContext, current));
                Assert.True(item.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
                PageScreenshots.Settle(window);
                Assert.Equal(page, workspace.CurrentPage);
                Assert.Same(current, rail.SelectedItem);
                Assert.True(item.IsSelected);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void APageOutsideTheRailStaysOpenAndReturningRestoresItsLocation(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 640;
                window.Height = 600;
                SettingsTestAccess.Model(window)
                    .ZoomPercent = zoom;
                workspace.CurrentPage = WorkspacePage.Overview;
                var rail = window.FindControl<ListBox>("PageList")!;
                rail.ItemsSource = workspace.VisiblePages.Where(page => page.Page != WorkspacePage.Texts).ToArray();
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Null(rail.SelectedItem);

                workspace.CurrentPage = WorkspacePage.Warnings;
                PageScreenshots.Settle(window);
                var current = workspace.PageOf(WorkspacePage.Warnings);
                Assert.Equal(WorkspacePage.Warnings, workspace.CurrentPage);
                Assert.Same(current, rail.SelectedItem);
                var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(control =>
                    ReferenceEquals(control.DataContext, current));
                Assert.True(item.Focus());
                window.KeyPress(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
                window.KeyRelease(Key.Space, RawInputModifiers.Control, PhysicalKey.Space, null);
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Warnings, workspace.CurrentPage);
                Assert.Same(current, rail.SelectedItem);
                Assert.True(item.IsSelected);
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    private sealed class PaneProvider(Control footer, Control? side = null) : IWorkspacePaneProvider
    {
        public string SidePanelTitle => "Texts";
        public Control? SidePanel => side;
        public Control InspectorFooter => footer;
    }

    private static void Click(Window window, Button button)
    {
        PageScreenshots.Settle(window);
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
    }

    private static void AssertInside(Window window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        var first = control.TranslatePoint(default, window)!.Value;
        var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(first.X >= -1 && first.Y >= -1 && last.X <= window.ClientSize.Width + 1 &&
            last.Y <= window.ClientSize.Height + 1, $"{control.Name ?? control.GetType().Name}: {first} to {last}");
    }
}
