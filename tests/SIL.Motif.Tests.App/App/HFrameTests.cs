using SIL.Motif.App;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless;
using SIL.Motif.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HFrameTests(AvaloniaHeadlessFixture avalonia)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedFrameIgnoresWindowResizesAndReceivesTheViewportOnReattachment(bool detachBeforeShow)
    {
        avalonia.Invoke(() =>
        {
            var window = new MainWindow { Width = 960, Height = 600 };
            var frame = Assert.IsAssignableFrom<Control>(window.Content);
            var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
            try
            {
                if (detachBeforeShow) window.Content = new TextBox();
                window.Show();
                PageScreenshots.Settle(window);
                if (detachBeforeShow) window.Content = frame;
                PageScreenshots.Settle(window);
                host.SetPagePanels(new PaneProvider(new Border(), new Button { Content = "Apply" }));
                var side = host.FindControl<Border>("SidePanelBorder")!;
                var inspector = host.FindControl<Border>("InspectorPane")!;
                Assert.False(side.IsVisible);
                Assert.False(inspector.IsVisible);

                window.Content = new TextBox();
                window.Width = 1300;
                window.Height = 1080;
                PageScreenshots.Settle(window);
                Assert.False(side.IsVisible);
                Assert.False(inspector.IsVisible);
                window.Content = frame;
                PageScreenshots.Settle(window);
                Assert.True(side.IsVisible);
                Assert.True(inspector.IsVisible);
                Assert.DoesNotContain("shortRail", window.FindInWindowOrWorkspace<ListBox>("PageList")!.Classes);

                window.Content = new TextBox();
                window.Width = 640;
                window.Height = 600;
                PageScreenshots.Settle(window);
                window.Content = frame;
                PageScreenshots.Settle(window);
                Assert.False(side.IsVisible);
                Assert.False(inspector.IsVisible);
                Assert.Contains("compact", window.FindInWindowOrWorkspace<Border>("StatusBar")!.Classes);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void TitleRailAndStatusLeaveThePageAStableWorkingArea()
    {
        avalonia.Invoke(() =>
        {
            var window = new MainWindow { Width = 1920, Height = 1080 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Equal(34, window.FindInWindowOrWorkspace<Border>("TopBar")!.Bounds.Height);
                Assert.Equal(44, window.FindInWindowOrWorkspace<Border>("Sidebar")!.Bounds.Width);
                var status = window.FindInWindowOrWorkspace<Border>("StatusBar");
                Assert.NotNull(status);
                Assert.Equal(24, status.Bounds.Height);
                Assert.Contains(status.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Tech demo: writes nothing until you press Apply");
                Assert.Contains(status.GetVisualDescendants().OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Report a problem");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public void PagePanesDetachOnNavigationAndInspectorCanHideAndReopen()
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border(), ObjectInspector = new TextBlock { Text = "Object details" } };
            var window = new MotifTestWindow { Width = 1920, Height = 1080, Content = host };
            var side = new TextBlock { Text = "Texts" };
            var footer = new Button { Content = "Apply" };
            try
            {
                window.Show();
                host.UpdateViewport(1920);
                host.SetObjectInspectorOpen(true);
                host.SetPagePanels(new PaneProvider(side, footer));
                PageScreenshots.Settle(window);
                Assert.Equal(248, host.FindControl<Border>("SidePanelBorder")!.Bounds.Width);
                var inspector = host.FindControl<Border>("InspectorPane")!;
                Assert.Equal(360, inspector.Bounds.Width);
                Assert.True(host.FindControl<ContentControl>("ObjectInspectorHost")!.IsVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                var hide = host.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetName(button) == "Hide Inspector");
                hide.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(window);
                Assert.False(inspector.IsVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                Assert.True(footer.IsEffectivelyVisible);
                var strip = host.FindControl<Button>("InspectorStrip")!;
                Assert.True(strip.IsVisible);
                Assert.Equal(24, strip.Bounds.Width);
                strip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(window);
                Assert.True(inspector.IsVisible);
                Assert.Same(footer, host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                Assert.Null(host.FindControl<ContentControl>("PageActionFooterHost")!.Content);
                host.SetPagePanels(null);
                PageScreenshots.Settle(window);
                Assert.False(host.FindControl<Border>("SidePanelBorder")!.IsVisible);
                Assert.Null(host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Null(host.FindControl<ContentControl>("InspectorFooterHost")!.Content);
                Assert.Null(side.Parent);
                Assert.Null(footer.Parent);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class PaneProvider(Control side, Control footer) : IWorkspacePaneProvider
    {
        public string SidePanelTitle => "Texts";
        public Control SidePanel => side;
        public Control InspectorFooter => footer;
    }

    [Fact]
    public void ManualReopeningSurvivesResizingUntilThePanelThresholdIsCrossed()
    {
        avalonia.Invoke(() =>
        {
            var host = new WorkspaceLayoutHost { Page = new Border() };
            var window = new MotifTestWindow { Width = 1920, Height = 1080, Content = host };
            try
            {
                window.Show();
                host.SetPagePanels(new PaneProvider(new Border(), new Button { Content = "Apply" }));
                host.UpdateViewport(640);
                var side = host.FindControl<Border>("SidePanelBorder")!;
                var inspector = host.FindControl<Border>("InspectorPane")!;
                Assert.False(side.IsVisible);
                Assert.False(inspector.IsVisible);
                host.ToggleSidePanel();
                host.FindControl<Button>("InspectorStrip")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                host.UpdateViewport(800);
                Assert.False(side.IsVisible);
                host.UpdateViewport(980);
                host.ToggleSidePanel();
                PageScreenshots.Settle(window);
                Assert.True(side.IsVisible);
                Assert.True(inspector.IsVisible);
                Assert.Equal(248, side.Bounds.Width);
                Assert.Equal(360, inspector.Bounds.Width);
                host.UpdateViewport(1100);
                host.UpdateViewport(800);
                Assert.True(side.IsVisible);
                Assert.False(inspector.IsVisible);
                host.UpdateViewport(1300);
                host.UpdateViewport(1100);
                Assert.False(side.IsVisible);
                Assert.True(inspector.IsVisible);
                Assert.Empty(host.GetVisualDescendants().OfType<GridSplitter>());
            }
            finally
            {
                window.Close();
            }
        });
    }
    [Fact]
    public void TextsPickerMovesToTheFrameAndItsRailButtonReopensIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 960;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var side = host.FindControl<Border>("SidePanelBorder")!;
                var selection = Assert.IsType<ContentControl>(host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Equal("SelectionHost", selection.Name);
                Assert.NotNull(selection.Content);
                Assert.False(side.IsVisible);
                var item = window.GetVisualDescendants().OfType<ListBoxItem>().Single(control =>
                    control.DataContext is PageViewModel { Page: WorkspacePage.Texts });
                var point = item.TranslatePoint(new Avalonia.Point(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                PageScreenshots.Settle(window);
                Assert.True(side.IsVisible);
                Assert.Equal(248, side.Bounds.Width);
                workspace.CurrentPage = WorkspacePage.Overview;
                Assert.Null(host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Null(selection.Parent);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void MinimumWindowKeepsTheRailAndStatusActionsInsideTheClientArea(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 640;
                window.Height = 600;
                var settings = SettingsTestAccess.Model(window);
                settings.ZoomPercent = zoom;
                PageScreenshots.Settle(window);
                Assert.Equal(640, window.ClientSize.Width);
                var actions = window.GetVisualDescendants().OfType<ListBoxItem>().Cast<Control>().ToList();
                actions.Add(window.FindInWindowOrWorkspace<Button>("HelpButton")!);
                actions.Add(window.FindInWindowOrWorkspace<Button>("SettingsButton")!);
                actions.Add(window.FindInWindowOrWorkspace<Border>("StatusBar")!.GetVisualDescendants().OfType<Button>().Single(button =>
                    button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Report a problem"));
                Assert.NotEmpty(actions);
                foreach (var action in actions)
                {
                    Assert.True(action.IsEffectivelyVisible);
                    var first = action.TranslatePoint(default, window)!.Value;
                    var last = action.TranslatePoint(new Point(action.Bounds.Width, action.Bounds.Height), window)!.Value;
                    Assert.True(first.X >= -1 && first.Y >= -1 && last.X <= window.ClientSize.Width + 1 &&
                        last.Y <= window.ClientSize.Height + 1, $"{action.Name ?? action.GetType().Name}: {first} to {last}");
                }
            }
            finally
            {
                WindowZoomPolicy.Apply(Application.Current!, ZoomPolicy.DefaultPercent);
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(1));
    }

}
