using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

internal static class SettingsTestAccess
{
    internal static SettingsViewModel Model(MainWindow window) =>
        Assert.IsType<SettingsViewModel>(Assert.IsType<WorkspaceShellViewModel>(window.DataContext)
            .PageModel<SettingsPageModel>().Settings);

    internal static SettingsPage Page(MainWindow window)
    {
        var page = window.PageView<SettingsPage>();
        Assert.Same(Model(window), page.DataContext);
        Assert.Same(Model(window), page.Navigation.DataContext);
        return page;
    }

    /// <summary>The shortcut rows, which exist once the list is open; a named control inside a template is not in the page's scope.</summary>
    internal static ItemsControl ShortcutList(SettingsPage page) =>
        page.GetVisualDescendants().OfType<ItemsControl>().Single(list => list.Name == "SettingsShortcutsItems");

    internal static SettingsPage Open(MainWindow window)
    {
        var workspace = Assert.IsType<WorkspaceShellViewModel>(window.DataContext);
        if (!window.IsVisible) window.Show();
        PageScreenshots.Settle(window);
        if (workspace.CurrentPage != WorkspacePage.Settings)
        {
            HeadlessClick.Click(window, window.FindInWindowOrWorkspace<Button>("SettingsButton")!, "Settings");
            PageScreenshots.Settle(window);
        }
        Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
        var page = Page(window);
        Assert.True(page.IsEffectivelyVisible);
        return page;
    }

    internal static void Select(SettingsPage page, SettingsGroup group)
    {
        var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
        var window = Assert.IsType<MainWindow>(TopLevel.GetTopLevel(page));
        var reopenNavigation = !page.Navigation.IsEffectivelyVisible;
        if (reopenNavigation)
        {
            HeadlessClick.Click(window, window.FindInWindowOrWorkspace<Button>("SettingsButton")!, "Settings");
            PageScreenshots.Settle(window);
        }
        Assert.True(page.Navigation.IsEffectivelyVisible);
        page.Navigation.FindControl<ListBox>("SettingsGroups")!.SelectedItem =
            Assert.Single(settings.PageGroupOptions, option => option.Group == group);
        PageScreenshots.Settle(window);
        Assert.Equal(group, settings.SelectedGroup);
        if (reopenNavigation)
        {
            var collapse = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "SidePanelCollapseButton" && button.IsEffectivelyVisible);
            HeadlessClick.Click(window, collapse, "Collapse side bar");
            PageScreenshots.Settle(window);
        }
        var heading = Section(page).GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Classes.Contains("settingsGroupHeading"));
        var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
        var transform = heading.TransformToVisual(scroll);
        Assert.NotNull(transform);
        var area = new Rect(heading.Bounds.Size).TransformToAABB(transform!.Value);
        Assert.True(area.Top >= -1 && area.Bottom <= scroll.Viewport.Height + 1,
            $"{group}: selected heading {area} is outside viewport {scroll.Viewport}; offset={scroll.Offset}.");
    }

    internal static Control Section(SettingsPage page) =>
        page.FindControl<StackPanel>($"Settings{Assert.IsType<SettingsViewModel>(page.DataContext).SelectedGroup}Section")!;

    internal static void AssertReachable(MainWindow window, Control control)
    {
        Assert.True(control.IsEffectivelyVisible);
        control.BringIntoView();
        PageScreenshots.Settle(window);
        var transform = control.TransformToVisual(window);
        Assert.NotNull(transform);
        var area = new Rect(control.Bounds.Size).TransformToAABB(transform!.Value);
        Assert.True(area.Width > 0 && area.Height > 0);
        Assert.True(new Rect(window.ClientSize).Contains(area), $"{control.Name}: {area} exceeds {window.ClientSize}.");
        foreach (var scroll in control.GetVisualAncestors().OfType<ScrollViewer>())
        {
            var scrollTransform = control.TransformToVisual(scroll);
            Assert.NotNull(scrollTransform);
            var scrollArea = new Rect(control.Bounds.Size).TransformToAABB(scrollTransform!.Value);
            Assert.InRange(scrollArea.Left, -1, scroll.Viewport.Width);
            Assert.InRange(scrollArea.Top, -1, scroll.Viewport.Height);
            Assert.True(scrollArea.Right <= scroll.Viewport.Width + 1);
            Assert.True(scrollArea.Bottom <= scroll.Viewport.Height + 1);
        }
    }
}
