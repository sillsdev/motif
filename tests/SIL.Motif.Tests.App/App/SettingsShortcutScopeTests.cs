using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsShortcutScopeTests
{
    [Theory]
    [InlineData(100)]
    [InlineData(150)]
    public void ShortcutRowsUseTheAvailablePageWidthAtFullSize(int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, frame) = FakeComposedWindow.Create();
            var model = workspace.PageModel<SettingsPageModel>();
            var settings = SettingsPageTests.WindowSettings(frame);
            model.Settings = settings;
            var page = Assert.IsType<SettingsPage>(PageRegistry.For(WorkspacePage.Settings).CreateView(model));
            var window = new Window
            {
                Width = 1920, Height = 1080,
                Content = new LayoutTransformControl
                {
                    Child = page, LayoutTransform = new Avalonia.Media.ScaleTransform(zoom / 100d, zoom / 100d),
                },
            };
            try
            {
                window.Show();
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                settings.OpenGroup(SettingsGroup.KeyboardShortcuts);
                page.ScrollToSelectedGroup();
                PageScreenshots.Settle(window);
                SettingsPageTests.CaptureSettings(window, $"scope-full-width-{zoom}");
                var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                var presenter = Assert.Single(scroll.GetVisualDescendants().OfType<ScrollContentPresenter>(),
                    candidate => ReferenceEquals(candidate.TemplatedParent, scroll));
                var list = Assert.IsType<StackPanel>(scroll.Content);
                var availableWidth = presenter.Bounds.Width - list.Margin.Left - list.Margin.Right;
                Assert.InRange(list.Bounds.Width, availableWidth - 1, availableWidth + 1);
                var row = page.FindControl<ItemsControl>("SettingsShortcutsItems")!
                    .GetVisualDescendants().OfType<Border>().First(border =>
                        border.Classes.Contains("settingsShortcutRow"));
                Assert.InRange(row.Bounds.Width, list.Bounds.Width - 1, list.Bounds.Width + 1);
                Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 1);
            }
            finally
            {
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(820, 600, 100)]
    [InlineData(820, 600, 150)]
    [InlineData(1040, 480, 150)]
    [InlineData(1920, 1080, 100)]
    public void IdenticalSearchGesturesShowTheirHumanReadableScopeInEachVisibleRow(int width, int height, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var previousTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, frame) = FakeComposedWindow.Create();
            var settings = SettingsPageTests.WindowSettings(frame);
            var model = workspace.PageModel<SettingsPageModel>();
            model.Settings = settings;
            var page = Assert.IsType<SettingsPage>(PageRegistry.For(WorkspacePage.Settings).CreateView(model));
            var window = new Window { Width = width, Height = height, Content = new LayoutTransformControl
            {
                Child = page, LayoutTransform = new Avalonia.Media.ScaleTransform(zoom / 100d, zoom / 100d),
            } };
            try
            {
                window.Show();
                workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
                var searchAction = KeyboardShortcutRegistry.Bindings.First(binding =>
                    binding.Behavior == KeyboardShortcutBehavior.FocusWordSearch).Row.Action;
                settings.ShortcutSearch = searchAction;
                settings.OpenGroup(SettingsGroup.KeyboardShortcuts);
                PageScreenshots.Settle(window);
                Assert.Equal(new[] { "Analyze texts", "Lists", "Matrix", "Word list" },
                    settings.ShortcutEntries.Select(entry => entry.Group).Order().ToArray());
                Assert.Single(settings.ShortcutEntries.Select(entry => entry.Keys).Distinct());
                Assert.Single(settings.ShortcutEntries.Select(entry => entry.Action).Distinct());
                SettingsPageTests.CaptureSettings(window, $"scope-duplicate-search-{width}-{height}-{zoom}");
                var list = page.FindControl<ItemsControl>("SettingsShortcutsItems")!;
                var rows = list.GetVisualDescendants().OfType<Border>()
                    .Where(row => row.Classes.Contains("settingsShortcutRow")).ToArray();
                Assert.Equal(4, rows.Length);
                foreach (var row in rows)
                {
                    var entry = Assert.IsType<ShortcutEntry>(row.DataContext);
                    row.BringIntoView();
                    PageScreenshots.Settle(window);
                    Assert.Contains(row.GetVisualDescendants().OfType<TextBlock>(), text =>
                        text.IsEffectivelyVisible && text.Text is { } visibleText &&
                        visibleText.Contains(entry.Group, StringComparison.Ordinal) &&
                        visibleText.Contains(entry.Action, StringComparison.Ordinal));
                    var scroll = page.FindControl<ScrollViewer>("SettingsListScroll")!;
                    var origin = row.TranslatePoint(default, scroll)!.Value;
                    Assert.InRange(origin.X, -1, scroll.Viewport.Width);
                    Assert.True(origin.X + row.Bounds.Width <= scroll.Viewport.Width + 1, entry.Group);
                    Assert.InRange(origin.Y, -1, scroll.Viewport.Height);
                    Assert.True(origin.Y + row.Bounds.Height <= scroll.Viewport.Height + 1, entry.Group);
                }
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                frame.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
