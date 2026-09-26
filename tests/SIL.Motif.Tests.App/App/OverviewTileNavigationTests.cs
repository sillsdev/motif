using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
    public void TextsRequestAppliesItsCellSelectionOnArrival()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                var cells = (IReadOnlyList<TextsListCell>)[
                    new TextsListCell(WordProjectStatus.Approved, CompareColumnKind.Match),
                    new TextsListCell(WordProjectStatus.Approved, CompareColumnKind.NoParse),
                ];
                var constructor = typeof(OpenTextsRequest).GetConstructor(
                    [typeof(TextsTab), typeof(IReadOnlyList<TextsListCell>)]);
                Assert.NotNull(constructor);

                workspace.Context.Open((PageRequest)constructor!.Invoke([TextsTab.Matrix, cells]));

                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(cells.ToHashSet(), workspace.PageModel<TextsPageModel>().Assess.Compare.Cells
                    .Where(cell => cell.IsSelected)
                    .Select(cell => new TextsListCell(cell.Row, cell.Column)).ToHashSet());
            }
            finally
            {
                Close(workspace, window);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TextCoverageTileClearsThePreviousMatrixFilter()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
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
            var (workspace, window) = FakeComposedWindow.Create();
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

}
