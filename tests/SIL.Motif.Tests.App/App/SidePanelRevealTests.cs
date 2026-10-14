using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SidePanelRevealTests
{
    [Theory]
    [InlineData(100, false)]
    [InlineData(100, true)]
    [InlineData(150, false)]
    [InlineData(150, true)]
    public void ALabelledTextsButtonOpensThePanelAndReceivesFocusWhenItCloses(int zoom, bool keyboard)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            var settings = Settings(window);
            try
            {
                window.Width = 640;
                window.Height = 600;
                settings.ZoomPercent = zoom;
                workspace.CurrentPage = WorkspacePage.Texts;
                PageScreenshots.Settle(window);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                var side = host.FindControl<Border>("SidePanelBorder")!;
                var reveal = host.FindControl<Button>("SidePanelRevealButton");
                Assert.NotNull(reveal);
                Assert.False(side.IsVisible);
                Assert.True(reveal.IsEffectivelyVisible);
                Assert.Contains(reveal.GetVisualDescendants().OfType<TextBlock>(), label => label.Text == "TEXTS");
                AssertInside(window, reveal);
                Activate(window, reveal, keyboard);
                PageScreenshots.Settle(window);
                Assert.True(side.IsEffectivelyVisible);
                Assert.False(reveal.IsVisible);
                var selector = Assert.IsType<ContentControl>(host.FindControl<ContentControl>("SidePanelContent")!.Content);
                Assert.Equal("SelectionHost", selector.Name);
                Assert.True(selector.IsEffectivelyVisible);
                var close = host.FindControl<Button>("SidePanelCollapseButton")!;
                AssertInside(window, close);
                Activate(window, close, keyboard);
                PageScreenshots.Settle(window);
                Assert.False(side.IsVisible);
                Assert.True(reveal.IsEffectivelyVisible);
                Assert.True(reveal.IsFocused);
                AssertInside(window, reveal);
            }
            finally
            {
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData(640, 100)]
    [InlineData(640, 150)]
    [InlineData(1600, 150)]
    public void EveryAvailableRailDestinationAcceptsPointerAndArrowNavigationInTheShortFrame(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false, advancedAiModeEnabled: true);
            var settings = Settings(window);
            try
            {
                window.Width = width;
                window.Height = 600;
                settings.ZoomPercent = zoom;
                PageScreenshots.Settle(window);
                Assert.Equal(width, window.ClientSize.Width);
                Assert.Equal(600, window.ClientSize.Height);
                var rail = window.FindControl<ListBox>("PageList")!;
                var entries = workspace.VisiblePages.ToArray();
                Assert.True(entries.Length >= 9);
                foreach (var entry in entries)
                {
                    rail.ScrollIntoView(entry);
                    PageScreenshots.Settle(window);
                    var item = RailItem(rail, entry);
                    AssertInside(window, item);
                    HeadlessClick.Click(window, item, entry.Title);
                    PageScreenshots.Settle(window);
                    Assert.Equal(entry.Page, workspace.CurrentPage);
                }
                Assert.True(RailItem(rail, entries[^1]).Focus(NavigationMethod.Directional));
                window.KeyPress(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                window.KeyRelease(Key.Home, RawInputModifiers.None, PhysicalKey.Home, null);
                PageScreenshots.Settle(window);
                Assert.Equal(entries[0].Page, workspace.CurrentPage);
                for (var index = 1; index < entries.Length; index++)
                {
                    Assert.True(RailItem(rail, entries[index - 1]).Focus(NavigationMethod.Directional));
                    window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                    PageScreenshots.Settle(window);
                    Assert.Equal(entries[index].Page, workspace.CurrentPage);
                    AssertInside(window, RailItem(rail, entries[index]));
                }
                AssertInside(window, window.FindControl<Button>("HelpButton")!);
                AssertInside(window, window.FindControl<Button>("SettingsButton")!);
            }
            finally
            {
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    [ScreenshotFact]
    public void TheCollapsedPanelKeepsItsLabelledRevealAcrossTheFrameMatrix()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
            Directory.CreateDirectory(folder);
            var priorTheme = Application.Current!.RequestedThemeVariant;
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            var settings = Settings(window);
            var failures = new List<string>();
            try
            {
                workspace.CurrentPage = WorkspacePage.Texts;
                foreach (var theme in new[] { "light", "dark" })
                foreach (var zoom in new[] { 100, 150 })
                foreach (var (size, width, height) in new[]
                {
                    ("thin", 640, 600), ("half", 960, 1040), ("full", 1920, 1080), ("short", 1600, 600),
                })
                {
                    window.Width = width;
                    window.Height = height;
                    settings.ZoomPercent = zoom;
                    Application.Current.RequestedThemeVariant = theme == "dark" ? ThemeVariant.Dark : ThemeVariant.Light;
                    PageScreenshots.Settle(window);
                    using var frame = window.CaptureRenderedFrame()
                        ?? throw new InvalidOperationException("No rendered Texts reveal frame was available.");
                    frame.Save(Path.Combine(folder, $"texts-reveal-{size}-{width}-{height}-{zoom}-{theme}.png"),
                        PngBitmapEncoderOptions.Default);
                    LayoutAssertions.AssertCurrent(window);
                    var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                    var sideVisible = host.FindControl<Border>("SidePanelBorder")!.IsVisible;
                    var reveal = host.FindControl<Button>("SidePanelRevealButton");
                    if (!sideVisible && reveal?.IsEffectivelyVisible != true)
                        failures.Add($"{size}/{zoom}/{theme}: Texts has no visible labelled side-panel reveal.");
                    if (sideVisible && reveal?.IsVisible == true)
                        failures.Add($"{size}/{zoom}/{theme}: the reveal remains after its side panel opens.");
                }
            }
            finally
            {
                settings.ZoomPercent = ZoomPolicy.DefaultPercent;
                Application.Current.RequestedThemeVariant = priorTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        }, TimeSpan.FromMinutes(4));
    }

    private static SettingsViewModel Settings(MainWindow window) =>
        SettingsTestAccess.Model(window);

    private static ListBoxItem RailItem(ListBox rail, PageViewModel entry) =>
        Assert.Single(rail.GetVisualDescendants().OfType<ListBoxItem>(), item => ReferenceEquals(item.DataContext, entry));

    private static void Activate(MainWindow window, Button button, bool keyboard)
    {
        if (!keyboard)
        {
            HeadlessClick.Click(window, button, AutomationProperties.GetName(button) ?? button.Name ?? "Panel control");
            return;
        }
        Assert.True(button.Focus(NavigationMethod.Tab));
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
        window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
    }

    private static void AssertInside(Window window, Control control)
    {
        var first = control.TranslatePoint(default, window)!.Value;
        var last = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        Assert.True(control.IsEffectivelyVisible && new Rect(window.ClientSize).Contains(new Rect(first, last)),
            $"{control.Name ?? control.GetType().Name}: {first} to {last} is outside {window.ClientSize}.");
    }
}
