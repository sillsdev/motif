using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Headless;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using Xunit;
using ContractWordRow = SIL.Motif.Contract.Responses.WordRow;

namespace SIL.Motif.Tests.App;

/// <summary>Clicks each menu handler in the headless window and reads the shortcut text the menu renders.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class MenuBarBehaviorTests
{
    private readonly AvaloniaHeadlessFixture _avalonia;

    public MenuBarBehaviorTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    private static void Click(MenuItem item) => item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

    private static MenuItem Item(MainWindow window, string name) => window.FindInWindowOrWorkspace<MenuItem>(name)!;

    private static string[] RenderedTexts(MainWindow window, string menuName, string itemName)
    {
        window.Show();
        Item(window, menuName).IsSubMenuOpen = true;
        PageScreenshots.Settle(window);
        return Item(window, itemName).GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text ?? "").ToArray();
    }

    [Theory]
    [InlineData("ToolsMenu", "OptionsMenuItem", "Ctrl+,")]
    [InlineData("HelpMenu", "KeyboardShortcutsMenuItem", "Shift+/")]
    [InlineData("ViewMenu", "ZoomInMenuItem", "Ctrl+=")]
    [InlineData("ViewMenu", "ZoomOutMenuItem", "Ctrl+-")]
    [InlineData("ViewMenu", "ResetZoomMenuItem", "Ctrl+0")]
    [InlineData("HelpMenu", "HelpTopicsMenuItem", "F1")]
    [InlineData("EditMenu", "UndoMenuItem", "Ctrl+Z")]
    [InlineData("EditMenu", "RedoMenuItem", "Ctrl+Y")]
    [InlineData("EditMenu", "CopyMenuItem", "Ctrl+C")]
    public void TheMenuRendersReadableShortcutText(string menu, string item, string expected)
    {
        if (OperatingSystem.IsMacOS()) return;
        _avalonia.Invoke(() =>
        {
            var window = new MainWindow();
            try
            {
                var texts = RenderedTexts(window, menu, item);
                Assert.Contains(expected, texts);
                Assert.DoesNotContain(texts, text => text.Contains("Oem", StringComparison.Ordinal));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ZoomItemsChangeTheZoomAndResetReturnsIt()
    {
        _avalonia.Invoke(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                var zoom = () => ((SettingsViewModel)workspace.PageModel<SettingsPageModel>().Settings!).ZoomPercent;
                var start = zoom();
                Click(Item(window, "ZoomInMenuItem"));
                Assert.True(zoom() > start);
                Click(Item(window, "ZoomOutMenuItem"));
                Click(Item(window, "ZoomOutMenuItem"));
                Assert.True(zoom() < start);
                Click(Item(window, "ResetZoomMenuItem"));
                Assert.Equal(ZoomPolicy.DefaultPercent, zoom());
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("UndoMenuItem", "There is nothing to undo.")]
    [InlineData("RedoMenuItem", "There is nothing to redo.")]
    public void UndoAndRedoReportThroughTheStatusLine(string item, string message)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                Click(Item(window, item));
                var line = window.FindInWindowOrWorkspace<TextBlock>("KeyboardShortcutStatusLine")!;
                for (var wait = 0; wait < 100 && line.Text != message; wait++) await Task.Delay(20);
                Assert.Equal(message, line.Text);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void CopyTakesTheWordThatWasFocusedBeforeTheMenuWasUsed()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                var word = new Button
                {
                    Focusable = true,
                    DataContext = new WordRowViewModel(new ContractWordRow("morpheme", WordRowOutcome.Same, "Kept", WordRowTone.Fine)),
                };
                window.FindInWindowOrWorkspace<Panel>("PageHost")!.Children.Add(word);
                PageScreenshots.Settle(window);
                Assert.True(word.Focus());
                Assert.True(Item(window, "EditMenu").Focus());
                Click(Item(window, "CopyMenuItem"));
                string? copied = null;
                for (var wait = 0; wait < 100 && copied is null; wait++)
                {
                    await Task.Delay(20);
                    copied = await window.Clipboard!.TryGetTextAsync();
                }
                Assert.Equal("morpheme", copied);
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(1240)]
    [InlineData(700)]
    public void CopyKeepsTheFocusedWordAfterFocusEntersTheEditPopup(int width)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Width = width;
                window.Show();
                await new AvaloniaClipboard(window).SetTextAsync("before-copy");
                var word = new Button
                {
                    Content = "morpheme",
                    DataContext = new WordRowViewModel(new ContractWordRow("morpheme", WordRowOutcome.Same, "Kept", WordRowTone.Fine)),
                };
                window.FindInWindowOrWorkspace<Panel>("PageHost")!.Children.Add(word);
                PageScreenshots.Settle(window);
                Assert.True(word.Focus());
                window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
                window.KeyPressQwerty(PhysicalKey.E, RawInputModifiers.Alt);
                window.KeyReleaseQwerty(PhysicalKey.E, RawInputModifiers.Alt);
                window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
                PageScreenshots.Settle(window);
                Assert.True(Item(window, "EditMenu").IsSubMenuOpen, "Alt+E must open the real Edit menu.");
                var copy = Item(window, "CopyMenuItem");
                copy.BringIntoView();
                PageScreenshots.Settle(window);
                Assert.True(copy.IsEffectivelyVisible);
                var popup = TopLevel.GetTopLevel(copy) ??
                    throw new InvalidOperationException("The open menu item must have an attached input root.");
                Assert.True(copy.Focus(NavigationMethod.Directional));
                Assert.True(copy.IsFocused);
                var clicked = false;
                copy.Click += (_, _) => clicked = true;
                popup.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                popup.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                PageScreenshots.Settle(window);
                Assert.True(clicked, "Enter must activate the Copy item in its popup.");
                Assert.Equal("morpheme", await window.Clipboard!.TryGetTextAsync());
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpaceChangesTheOpenRecentPageInBothDirections(bool previous)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            var directory = Path.Combine(Path.GetTempPath(), "MotifRecentMenu", Guid.NewGuid().ToString("N"));
            fake.KnownProjectsListIs(Enumerable.Range(0, 45).Select(index =>
                new KnownProjectSummary(Path.Combine(directory, $"project-{index:D2}.fwdata"),
                    DateTimeOffset.UnixEpoch.AddMinutes(-index))).ToArray());
            var (workspace, window) = FakeComposedWindow.Create(fake);
            try
            {
                window.Width = 1240;
                window.Show();
                await workspace.Project.LoadKnownProjectsAsync();
                Assert.Equal(45, workspace.RecentProjects.Count);
                var ordered = workspace.RecentProjects.Select(project => project.FullFwDataPath).ToArray();
                Assert.True(window.FindInWindowOrWorkspace<Button>("ProjectChip")!.Focus());
                window.KeyPress(Key.LeftAlt, RawInputModifiers.Alt, PhysicalKey.AltLeft, null);
                window.KeyPressQwerty(PhysicalKey.F, RawInputModifiers.Alt);
                window.KeyReleaseQwerty(PhysicalKey.F, RawInputModifiers.Alt);
                window.KeyRelease(Key.LeftAlt, RawInputModifiers.None, PhysicalKey.AltLeft, null);
                PageScreenshots.Settle(window);
                Assert.True(Item(window, "FileMenu").IsSubMenuOpen);
                var recent = Item(window, "OpenRecentMenuItem");
                Assert.True(recent.Focus(NavigationMethod.Directional));
                var filePopup = TopLevel.GetTopLevel(recent) ??
                    throw new InvalidOperationException("The open menu item must have an attached input root.");
                filePopup.KeyPress(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                filePopup.KeyRelease(Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight, null);
                PageScreenshots.Settle(window);
                Assert.True(recent.IsSubMenuOpen, "Right must open the actual Open Recent popup.");
                Assert.Equal(ordered.Take(20), ShownPaths());
                if (previous)
                {
                    Activate("next", Key.Enter, PhysicalKey.Enter);
                    Assert.Equal(ordered.Skip(20).Take(20), ShownPaths());
                }
                Activate(previous ? "previous" : "next", Key.Space, PhysicalKey.Space);
                Assert.True(recent.IsSubMenuOpen, "Paging must keep Open Recent open.");
                Assert.Equal(previous ? ordered.Take(20) : ordered.Skip(20).Take(20), ShownPaths());

                IEnumerable<string> ShownPaths() => window.RecentProjectItems
                    .Select(item => ((RecentProjectViewModel)item.CommandParameter!).FullFwDataPath).ToArray();

                void Activate(string direction, Key key, PhysicalKey physicalKey)
                {
                    var paging = Assert.Single(recent.Items.OfType<MenuItem>(), item => Equals(item.Tag, direction));
                    paging.BringIntoView();
                    PageScreenshots.Settle(window);
                    Assert.True(paging.IsEffectivelyVisible);
                    Assert.True(paging.Focus(NavigationMethod.Directional));
                    Assert.True(paging.IsFocused);
                    var popup = TopLevel.GetTopLevel(paging) ??
                        throw new InvalidOperationException("The open menu item must have an attached input root.");
                    popup.KeyPress(key, RawInputModifiers.None, physicalKey, null);
                    popup.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
                    PageScreenshots.Settle(window);
                }
            }
            finally
            {
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void HelpTopicsOpensTheHelpFlyoutAndExitClosesTheWindow()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = FakeComposedWindow.Create();
            var closing = false;
            window.Closing += (_, _) => closing = true;
            try
            {
                window.Show();
                var help = window.FindInWindowOrWorkspace<Button>("HelpButton")!;
                Click(Item(window, "HelpTopicsMenuItem"));
                PageScreenshots.Settle(window);
                Assert.True(help.Flyout!.IsOpen);
                Click(Item(window, "ExitMenuItem"));
                Assert.True(closing);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TheMenuHasAnAccessibleNameAndTheTitleBarNameReceivesPointerInput()
    {
        _avalonia.Invoke(() =>
        {
            var (_, window) = FakeComposedWindow.Create();
            try
            {
                Assert.Equal("Main menu", AutomationProperties.GetName(window.FindInWindowOrWorkspace<Menu>("MainMenu")!));
                Assert.Equal(WindowDecorationsElementRole.User,
                    WindowDecorationProperties.GetElementRole(window.FindInWindowOrWorkspace<Control>("ProjectChip")!));
                Assert.NotNull(AutomationProperties.GetHelpText(window.FindInWindowOrWorkspace<MenuItem>("ConfigureMenuItem")!));
            }
            finally { window.Close(); }
        });
    }
}
