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
    public void SceneSaveRejectsWrongPageAndPageHostStateBeforeWritingPng()
    {
        var root = CreateCaptureFolder();
        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
                try
                {
                    var progressView = window.PageView(WorkspacePage.Progress);
                    var textsView = window.PageView(WorkspacePage.Texts);

                    await AssertSceneSaveRejected(root, window, workspace, "wrong-current-page",
                        WorkspacePage.Texts, TextsTab.AnalyzeTexts, "Expected CurrentPage Texts");

                    workspace.CurrentPage = WorkspacePage.Progress;
                    PageScreenshots.Settle(window);
                    progressView.IsVisible = false;
                    PageScreenshots.Settle(window);
                    await AssertSceneSaveRejected(root, window, workspace, "invisible-page-view",
                        WorkspacePage.Progress, null, "Expected exactly one visible named page");

                    textsView.IsVisible = true;
                    PageScreenshots.Settle(window);
                    await AssertSceneSaveRejected(root, window, workspace, "wrong-active-page-view",
                        WorkspacePage.Progress, null, "Expected ProgressPage to be visible");

                    progressView.IsVisible = true;
                    textsView.IsVisible = false;
                    progressView.DataContext = workspace.PageModel<TextsPageModel>();
                    var wrongModelPath = Path.Combine(root, "wrong-page-model.png");
                    var modelFailure = Assert.ThrowsAny<XunitException>(() => MatrixCaptureState.SaveFrame(window,
                        workspace, WorkspacePage.Progress, null, wrongModelPath));
                    Assert.Contains("not bound to the selected page model", modelFailure.Message);
                    Assert.False(File.Exists(wrongModelPath));

                    progressView.DataContext = workspace.PageModel<ProgressPageModel>();
                    PageScreenshots.Settle(window);
                    var standardPath = Path.Combine(root, "standard-page-loop.png");
                    var standardFailure = Assert.ThrowsAny<XunitException>(() => PageScreenshots.SavePageFrame(window,
                        workspace, WorkspacePage.Texts, TextsTab.AnalyzeTexts, standardPath));
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
    public void SceneSaveWritesImagesForExplicitProgressAndTextsStates()
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
                    await CaptureScene(root, window, workspace, "progress-good", WorkspacePage.Progress, null, sizes);

                    workspace.CurrentPage = WorkspacePage.Texts;
                    await CaptureScene(root, window, workspace, "texts-reader-good", WorkspacePage.Texts,
                        TextsTab.AnalyzeTexts, sizes);

                    AssertPngExists(root, "progress-good", "progress-good-full-100-light.png");
                    AssertPngExists(root, "texts-reader-good", "texts-reader-good-full-100-light.png");
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
                    await CaptureScene(root, window, workspace, "overview-tip", WorkspacePage.Progress, null,
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
