using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Assistant;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SettingsTwoWindowsTests
{
    [Theory]
    [InlineData(1400, 100)]
    [InlineData(640, 100)]
    [InlineData(640, 150)]
    public void ClaudeOpensAndClosesWithoutChangingPageAndReturnsFocus(int width, int zoom)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Width = width;
                window.Height = 700;
                workspace.PageModel<SettingsPageModel>().Settings!.ZoomPercent = zoom;
                window.Show();
                workspace.Context.OpenPage(WorkspacePage.Words);
                PageScreenshots.Settle(window);
                var toggle = window.GetVisualDescendants().OfType<ClaudePanelButton>().Single();
                toggle.Focus();
                HeadlessClick.Click(window, toggle, "Claude");
                PageScreenshots.Settle(window);
                var layout = window.GetVisualDescendants().OfType<WorkspaceLayoutHost>().Single();
                var panel = Assert.IsType<ClaudePanel>(layout.ClaudePanel);
                Assert.Equal(WorkspacePage.Words, workspace.CurrentPage);
                var close = panel.FindControl<Button>("CloseButton")!;
                Assert.True(close.IsEffectivelyVisible);
                var bounds = new Rect(close.Bounds.Size).TransformToAABB(close.TransformToVisual(window)!.Value);
                Assert.InRange(bounds.Left, 0, window.ClientSize.Width);
                Assert.InRange(bounds.Right, 0, window.ClientSize.Width + 1);
                HeadlessClick.Click(window, close, "Close Claude Panel");
                PageScreenshots.Settle(window);
                Assert.Null(layout.ClaudePanel);
                Assert.True(toggle.IsFocused);
                Assert.Equal(WorkspacePage.Words, workspace.CurrentPage);
                HeadlessClick.Click(window, toggle, "Claude");
                PageScreenshots.Settle(window);
                Assert.NotNull(layout.ClaudePanel);
                HeadlessClick.Click(window, toggle, "Claude");
                PageScreenshots.Settle(window);
                Assert.Null(layout.ClaudePanel);
                Assert.Equal(WorkspacePage.Words, workspace.CurrentPage);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SettingsHostsConnectionsAndDiagnosticsWithoutASecondSourcesEditor()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, window) = FakeComposedWindow.Create(preferencesStore: preferences);
            try
            {
                window.Show();
                var page = SettingsTestAccess.Open(window);
                PageScreenshots.Settle(window);
                var settings = Assert.IsType<SettingsViewModel>(page.DataContext);
                Assert.Null(page.FindControl<StackPanel>("SettingsTextsSection"));
                Assert.Null(page.FindControl<Button>("UseAllTextsButton"));
                var connections = Assert.Single(page.GetVisualDescendants().OfType<SIL.Motif.App.Views.Settings.AssistantConnectionsPanel>());
                Assert.Same(workspace.PageModel<SettingsPageModel>().Connections, connections.DataContext);
                Assert.False(settings.Diagnostics);
                settings.Diagnostics = true;
                Assert.True(preferences.Current.Diagnostics);
                settings.Diagnostics = false;
                Assert.False(workspace.Context.Diagnostics);
                Assert.False(preferences.Current.Diagnostics);
                workspace.Context.Diagnostics = true;
                Assert.True(settings.Diagnostics);
                var toggle = window.GetVisualDescendants().OfType<ClaudePanelButton>().Single();
                HeadlessClick.Click(window, toggle, "Claude");
                PageScreenshots.Settle(window);
                var setup = window.GetVisualDescendants().OfType<Button>().Single(button =>
                    AutomationProperties.GetAutomationId(button) == AutomationIds.ClaudeSetup);
                HeadlessClick.Click(window, setup, "Set Up Claude in Settings");
                PageScreenshots.Settle(window);
                Assert.Equal(WorkspacePage.Settings, workspace.CurrentPage);
                Assert.Equal(SIL.Motif.App.ViewModels.SettingsGroup.AssistantConnections, settings.SelectedGroup);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromMinutes(2));
    }

    [Theory]
    [InlineData("proposal")]
    [InlineData("compare")]
    [InlineData("guide")]
    public void UnsupportedContextualButtonsAreHidden(string kind)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var button = new AskAssistantButton { TargetKind = kind, TargetId = "id" };
            Assert.False(button.FindControl<Button>("AskButton")!.IsVisible);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(30));
    }
}
