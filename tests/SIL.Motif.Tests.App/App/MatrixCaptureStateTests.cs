using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;
using Xunit.Sdk;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class MatrixCaptureStateTests
{
    [Fact]
    public void SceneSaveRejectsWrongPageTabAndPageHostStateBeforeWritingPng()
    {
        var root = CreateCaptureFolder();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
                try
                {
                    var overviewView = window.PageView(WorkspacePage.Overview);
                    var textsView = window.PageView(WorkspacePage.Texts);

                    await AssertSceneSaveRejected(root, window, workspace, "wrong-current-page",
                        WorkspacePage.Texts, TextsTab.Matrix, "Expected CurrentPage Texts");

                    workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                    workspace.CurrentPage = WorkspacePage.Texts;
                    PageScreenshots.Settle(window);
                    await AssertSceneSaveRejected(root, window, workspace, "wrong-texts-tab",
                        WorkspacePage.Texts, TextsTab.AnalyzeTexts, "Expected Texts tab AnalyzeTexts");

                    workspace.CurrentPage = WorkspacePage.Overview;
                    PageScreenshots.Settle(window);
                    overviewView.IsVisible = false;
                    PageScreenshots.Settle(window);
                    await AssertSceneSaveRejected(root, window, workspace, "invisible-page-view",
                        WorkspacePage.Overview, null, "Expected exactly one visible named page");

                    textsView.IsVisible = true;
                    PageScreenshots.Settle(window);
                    await AssertSceneSaveRejected(root, window, workspace, "wrong-active-page-view",
                        WorkspacePage.Overview, null, "Expected OverviewPage to be visible");

                    overviewView.IsVisible = true;
                    textsView.IsVisible = false;
                    overviewView.DataContext = workspace.PageModel<TextsPageModel>();
                    var wrongModelPath = Path.Combine(root, "wrong-page-model.png");
                    var modelFailure = Assert.ThrowsAny<XunitException>(() => MatrixCaptureState.SaveFrame(window,
                        workspace, WorkspacePage.Overview, null, wrongModelPath));
                    Assert.Contains("not bound to the selected page model", modelFailure.Message);
                    Assert.False(File.Exists(wrongModelPath));

                    overviewView.DataContext = workspace.PageModel<OverviewPageModel>();
                    PageScreenshots.Settle(window);
                    var standardPath = Path.Combine(root, "standard-page-loop.png");
                    var standardFailure = Assert.ThrowsAny<XunitException>(() => PageScreenshots.SavePageFrame(window,
                        workspace, WorkspacePage.Texts, TextsTab.Matrix, standardPath));
                    Assert.Contains("Expected CurrentPage Texts", standardFailure.Message);
                    Assert.False(File.Exists(standardPath));
                }
                finally
                {
                    window.Close();
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SceneSaveWritesImagesForExplicitOverviewAndTextsStates()
    {
        var root = CreateCaptureFolder();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
                try
                {
                    var sizes = new[] { new MatrixSize("full", 900, 700) };
                    await CaptureScene(root, window, workspace, "overview-good", WorkspacePage.Overview, null, sizes);

                    workspace.PageModel<TextsPageModel>().Tab = TextsTab.Matrix;
                    workspace.CurrentPage = WorkspacePage.Texts;
                    await CaptureScene(root, window, workspace, "texts-matrix-good", WorkspacePage.Texts,
                        TextsTab.Matrix, sizes);

                    AssertPngExists(root, "overview-good", "overview-good-full-100-light.png");
                    AssertPngExists(root, "texts-matrix-good", "texts-matrix-good-full-100-light.png");
                }
                finally
                {
                    window.Close();
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CaptureMovesThePointerAwayFromAnOpenTooltip()
    {
        var root = CreateCaptureFolder();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
                var owner = window.FindInWindowOrWorkspace<Button>("SettingsButton")!;
                try
                {
                    window.Width = 900;
                    window.Height = 700;
                    PageScreenshots.Settle(window);
                    ToolTip.SetShowDelay(owner, 0);
                    window.MouseMove(ClearTipPlacement.AreaOf(owner, window).Center);
                    PageScreenshots.Settle(window);
                    await Task.Yield();
                    PageScreenshots.Settle(window);
                    Assert.True(ToolTip.GetIsOpen(owner));
                    await CaptureScene(root, window, workspace, "overview-tip", WorkspacePage.Overview, null,
                        [new MatrixSize("full", 900, 700)]);
                    Assert.False(ToolTip.GetIsOpen(owner));
                }
                finally
                {
                    ToolTip.SetIsOpen(owner, false);
                    owner.ClearValue(ToolTip.ShowDelayProperty);
                    window.Close();
                    await workspace.DisposeAsync();
                }
            }, TimeSpan.FromMinutes(3));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertSceneSaveRejected(string root, MainWindow window,
        WorkspaceShellViewModel workspace, string scene, WorkspacePage expectedPage, TextsTab? expectedTab,
        string expectedFailure)
    {
        var run = NewRun(root, scene);
        var path = Path.Combine(run.MatrixFolder, $"{scene}-guard-100-light.png");
        var failure = await Assert.ThrowsAnyAsync<XunitException>(() => SizeMatrixScreenshots.CaptureScene(
            run, window, workspace, scene, expectedPage, expectedTab,
            [new MatrixSize("guard", 900, 700)], Selection(scene, "guard")));
        Assert.Contains(expectedFailure, failure.Message);
        Assert.False(File.Exists(path));
    }

    private static async Task CaptureScene(string root, MainWindow window, WorkspaceShellViewModel workspace,
        string scene, WorkspacePage expectedPage, TextsTab? expectedTab, IReadOnlyList<MatrixSize> sizes)
    {
        var run = NewRun(root, scene);
        await SizeMatrixScreenshots.CaptureScene(run, window, workspace, scene, expectedPage, expectedTab,
            sizes, Selection(scene, sizes[0].Name));
    }

    private static MatrixRun NewRun(string root, string scene) => new(Path.Combine(root, scene));

    private static MatrixCaptureSelection Selection(string scene, string size) =>
        new(new HashSet<string>([scene], StringComparer.Ordinal),
            [new MatrixSelectionPoint(size, 100, "light")]);

    private static void AssertPngExists(string root, string scene, string fileName)
    {
        var path = Path.Combine(root, scene, "matrix", fileName);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8).ToArray());
    }

    private static string CreateCaptureFolder() =>
        Directory.CreateTempSubdirectory("motif-matrix-capture-state-").FullName;
}
