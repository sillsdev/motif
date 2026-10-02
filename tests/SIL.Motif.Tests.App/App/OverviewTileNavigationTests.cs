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
    [Theory]
    [InlineData("Open Speed in Timing", WorkspacePage.Timing)]
    [InlineData("Open Grammar warnings in Warnings", WorkspacePage.Warnings)]
    [InlineData("Start an AI Handoff", WorkspacePage.AiHandoff)]
    public void OverviewDetailTilesOpenTheirPages(string tileName, WorkspacePage expectedPage)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                workspace.PageModel<OverviewPageModel>().Overview = OverviewPageWordsTests.Populated();
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                FakeComposedWindow.Click(window, tileName);

                Assert.Equal(expectedPage, workspace.CurrentPage);
            }
            finally
            {
                Close(workspace, window);
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

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
                workspace.Context.Open(new OpenTextsRequest(TextsTab.Matrix, cells));

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
    public void LookFirstRowOpensItsMeasuredWordsFromAnywhereInTheRow()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                workspace.PageModel<OverviewPageModel>().Overview = OverviewPageWordsTests.Populated();
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var row = Assert.Single(window.GetLogicalDescendants().OfType<Button>(), link =>
                    link.DataContext is OverviewLookFirstRow lookFirst && lookFirst.LinkText == "See the 6 words");
                HeadlessClick.Click(window, row, "Look first row");

                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);
                Assert.True(workspace.PageModel<TextsPageModel>().Assess.Compare.Cells.Single(cell =>
                    cell.Row == WordProjectStatus.Approved && cell.Column == CompareColumnKind.NoParse).IsSelected);
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
                workspace.PageModel<OverviewPageModel>().Overview = OverviewPageWordsTests.Populated();
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();
                var compare = workspace.PageModel<TextsPageModel>().Assess.Compare;
                compare.SelectCells([new TextsListCell(WordProjectStatus.Candidate, CompareColumnKind.Match)]);

                FakeComposedWindow.Click(window, "Open Text coverage in Texts");

                Assert.Equal(WorkspacePage.Texts, workspace.CurrentPage);
                Assert.Equal(TextsTab.Matrix, workspace.PageModel<TextsPageModel>().Tab);
                Assert.DoesNotContain(compare.Cells, cell => cell.IsSelected);
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
                workspace.PageModel<OverviewPageModel>().Overview = OverviewPageWordsTests.Populated();
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                FakeComposedWindow.Click(window, "Open Approved analyses kept in the Matrix");

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

    private static void Close(WorkspaceShellViewModel workspace, MainWindow window)
    {
        window.Close();
        workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
