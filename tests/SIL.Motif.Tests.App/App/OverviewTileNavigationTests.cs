using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewTileNavigationTests
{
    [Fact]
    public void TextCoverageTileClearsThePreviousMatrixFilter()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = CreateWindow();
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                var compare = workspace.PageModel<TextsPageModel>().Assess.Compare;
                compare.SelectCells([new TextsListCell(WordProjectStatus.Candidate, CompareColumnKind.Match)]);

                Click(window, "Open Text Coverage in Texts");

                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);
                Assert.Empty(compare.Cells.Where(cell => cell.IsSelected));
            }
            finally
            {
                Close(workspace, window);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void AccuracyTileSelectsApprovedWordsInTheMatrix()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = CreateWindow();
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                Click(window, "Open accuracy in Texts");

                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);
                Assert.Equal(Enum.GetValues<CompareColumnKind>()
                        .Select(column => new TextsListCell(WordProjectStatus.Approved, column)).ToHashSet(),
                    workspace.PageModel<TextsPageModel>().Assess.Compare.Cells.Where(cell => cell.IsSelected)
                        .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet());
            }
            finally
            {
                Close(workspace, window);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static (WorkspaceShellViewModel Workspace, MainWindow Window) CreateWindow()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var window = new MainWindow();
        var workspace = new WorkspaceShellViewModel(
            new ProjectViewModel(fake, new NoProjectPicker()),
            new BaselineViewModel(fake),
            selection,
            new AssessViewModel(fake, selection),
            new NoFolderPicker(), new NoDragSource(), fake,
            clipboard: new AvaloniaClipboard(window));
        window.Compose(workspace);
        return (workspace, window);
    }

    private static void Click(MainWindow window, string name)
    {
        var button = window.GetLogicalDescendants().OfType<Button>().Single(control =>
            string.Equals(AutomationProperties.GetName(control), name, StringComparison.Ordinal));
        HeadlessClick.Click(window, button, name);
    }

    private static void Close(WorkspaceShellViewModel workspace, MainWindow window)
    {
        window.Close();
        workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private sealed class NoProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoFolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class NoDragSource : IFileDragSource
    {
        public Task<Avalonia.Input.DragDropEffects> StartDragAsync(
            Avalonia.Input.PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths,
            Avalonia.Input.DragDropEffects allowedEffects) => Task.FromResult(Avalonia.Input.DragDropEffects.None);
    }
}
