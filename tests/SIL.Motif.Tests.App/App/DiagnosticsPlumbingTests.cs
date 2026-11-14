using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins diagnostic page visibility and the per-user choice.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class DiagnosticsPlumbingTests
{
    private static WorkspaceShellViewModel NewWorkspace()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        return new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new NoPicker()), new BaselineViewModel(fake), selection,
            new AssessViewModel(fake, selection), new NoPicker(), new NoPicker(), fake, pageEntries: PageRegistry.Entries);
    }

    [Theory]
    [InlineData(WorkspacePage.Warnings)]
    [InlineData(WorkspacePage.Parsimony)]
    [InlineData(WorkspacePage.Timing)]
    public void ADiagnosticPageLeavesTheRailWhenDisabledAndComesBack(WorkspacePage diagnostic)
    {
        var workspace = NewWorkspace();
        Assert.False(workspace.Context.Diagnostics);
        workspace.Context.Diagnostics = true;
        Assert.Contains(workspace.VisiblePages, page => page.Page == diagnostic);
        var changes = new List<string?>();
        workspace.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        workspace.Context.Diagnostics = false;

        Assert.DoesNotContain(workspace.VisiblePages, page => page.Page == diagnostic);
        Assert.Contains(nameof(WorkspaceShellViewModel.VisiblePages), changes);
        Assert.False(workspace.Context.CanOpenPage(diagnostic));
        workspace.Context.OpenPage(diagnostic);
        Assert.NotEqual(diagnostic, workspace.CurrentPage);

        workspace.Context.Diagnostics = true;

        Assert.Contains(workspace.VisiblePages, page => page.Page == diagnostic);
        Assert.True(workspace.Context.CanOpenPage(diagnostic));
    }

    [Fact]
    public void TheSelectedPageStaysWhenStillVisibleAndOtherwiseFallsBackToOverview()
    {
        var workspace = NewWorkspace();
        workspace.CurrentPage = WorkspacePage.Texts;
        workspace.Context.Diagnostics = false;
        Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);

        workspace.Context.Diagnostics = true;
        workspace.CurrentPage = WorkspacePage.Parsimony;
        workspace.Context.Diagnostics = false;

        Assert.Equal(WorkspacePage.Overview, workspace.CurrentPage);
        Assert.Equal(WorkspacePage.Overview, workspace.SelectedPage.Page);
    }

    [Fact]
    public void DiagnosticsRoundTripsThroughPreferencesAndDefaultsOff()
    {
        var folder = Directory.CreateTempSubdirectory("motif-diagnostics-").FullName;
        try
        {
            var path = Path.Combine(folder, "user-preferences.json");
            var store = new FileUserPreferencesStore(path);
            Assert.False(store.Current.Diagnostics);

            var workspace = NewWorkspace();
            workspace.Context.UseDiagnosticsPreferences(store);
            workspace.Context.Diagnostics = true;

            var reread = new FileUserPreferencesStore(path);
            Assert.Equal(PreferenceState.Ready, reread.State);
            Assert.True(reread.Current.Diagnostics);
            Assert.Equal(store.Current, reread.Current);

            var next = NewWorkspace();
            next.Context.UseDiagnosticsPreferences(reread);
            Assert.True(next.Context.Diagnostics);
            Assert.Contains(next.VisiblePages, page => page.Page == WorkspacePage.Parsimony);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void ViewMenuTogglesAndPersistsDiagnostics()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var preferences = new MemoryUserPreferencesStore();
            var (workspace, window) = FakeComposedWindow.Create(preferencesStore: preferences);
            try
            {
                window.Show();
                var view = window.FindInWindowOrWorkspace<MenuItem>("ViewMenu")!;
                view.IsSubMenuOpen = true;
                PageScreenshots.Settle(window);
                var item = window.FindInWindowOrWorkspace<MenuItem>("DiagnosticsMenuItem")!;
                Assert.False(item.IsChecked);
                HeadlessClick.Click(window, item, "Diagnostics");
                Assert.True(workspace.Context.Diagnostics);
                Assert.True(preferences.Current.Diagnostics);
                Assert.True(item.IsChecked);
                view.IsSubMenuOpen = true;
                PageScreenshots.Settle(window);
                HeadlessClick.Click(window, item, "Diagnostics");
                Assert.False(workspace.Context.Diagnostics);
                Assert.False(preferences.Current.Diagnostics);
                Assert.False(item.IsChecked);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void DiagnosticsGroupHeadingFollowsTheRailWidth()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                workspace.Context.Diagnostics = true;
                window.Width = 1400;
                window.Show();
                PageScreenshots.Settle(window);
                Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Diagnostics" && text.Classes.Contains("pageLabel") && text.IsEffectivelyVisible);

                window.Width = 640;
                PageScreenshots.Settle(window);
                Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.Text == "Diagnostics" && text.Classes.Contains("pageLabel") && text.IsEffectivelyVisible);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TryWordKeepsItsResultsAndHidesTraceUntilDiagnosticsIsEnabled()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var workspace = NewWorkspace();
            var page = workspace.PageModel<TryWordPageModel>();
            var panel = new TryWordPanel(page);
            var window = new MotifTestWindow { Content = panel };
            try
            {
                window.Show();
                PageScreenshots.SettleLayout(window);
                Assert.Contains(workspace.VisiblePages, entry => entry.Page == WorkspacePage.TryAWord);
                Assert.True(panel.FindControl<Control>("TryWordInputBox")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Control>("TryWordModeButtons")!.IsEffectivelyVisible);
                Assert.False(panel.FindControl<Control>("TraceDetails")!.IsEffectivelyVisible);
                Assert.False(panel.AttemptInspector.IsVisible);
                workspace.Context.ToggleDiagnosticsCommand.Execute(null);
                PageScreenshots.SettleLayout(window);
                Assert.True(panel.FindControl<Control>("TryWordModeButtons")!.IsEffectivelyVisible);
                Assert.True(panel.FindControl<Control>("TraceDetails")!.IsEffectivelyVisible);
                Assert.True(panel.AttemptInspector.IsVisible);
            }
            finally { window.Close(); await workspace.DisposeAsync(); }
        }, TimeSpan.FromSeconds(30));
    }

    private sealed class NoPicker : IProjectPicker, IHandoffFolderPicker, IFileDragSource
    {
        Task<string?> IProjectPicker.PickProjectFileAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        Task<string?> IHandoffFolderPicker.PickFolderAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);

        Task<DragDropEffects> IFileDragSource.StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
