using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>Runs only when <c>MOTIF_SCREENSHOTS</c> names a folder and <c>MOTIF_CAPTURE_PROJECT</c> a <c>.fwdata</c>.</summary>
public sealed class RealProjectCaptureFactAttribute : FactAttribute
{
    public const string ProjectVariable = "MOTIF_CAPTURE_PROJECT";

    public RealProjectCaptureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)) ||
            !File.Exists(Environment.GetEnvironmentVariable(ProjectVariable)))
            Skip = $"Set {ScreenshotFactAttribute.FolderVariable} and {ProjectVariable} (a .fwdata file) to capture one real project.";
    }
}

/// <summary>
/// Opens one real project in the real window, runs an Assessment with the real parser, and saves the main
/// pages at 1600x1000, 100% zoom, light theme. The project is copied first, so the original is never opened.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class RealProjectReadmeCapture(ITestOutputHelper output)
{
    private const int WordBudget = 150;

    [RealProjectCaptureFact]
    public void CaptureMainPagesOfOneRealProject()
    {
        var original = Environment.GetEnvironmentVariable(RealProjectCaptureFactAttribute.ProjectVariable)!;
        var folder = Path.Combine(Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!, "real-project");
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(original);
        var scratch = Path.Combine(Path.GetTempPath(), "SIL.Motif.RealScreens", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(scratch, "projects", name);
        var managedRoot = Path.Combine(scratch, "managed");
        var handoffFolder = Path.Combine(scratch, "handoff");
        foreach (var directory in new[] { projectDirectory, managedRoot, handoffFolder }) Directory.CreateDirectory(directory);
        var fwDataPath = Path.Combine(projectDirectory, name + ".fwdata");
        File.Copy(original, fwDataPath);
        foreach (var shared in new[] { "WritingSystemStore", "SharedSettings" })
        {
            var from = Path.Combine(Path.GetDirectoryName(original)!, shared);
            if (Directory.Exists(from)) CopyDirectory(from, Path.Combine(projectDirectory, shared));
        }

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var walkthrough = new WalkthroughWindow(managedRoot, fwDataPath, handoffFolder);
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                walkthrough.Window.Width = 1600;
                walkthrough.Window.Height = 1000;
                SettingsTestAccess.Model(walkthrough.Window).ZoomPercent = 100;
                walkthrough.Show();
                await Capture(walkthrough, folder);
            }, TimeSpan.FromMinutes(30));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(scratch);
        }
    }

    private async Task Capture(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        await workspace.SetProjectAsync(walkthrough.ProjectPath);
        walkthrough.WaitUntil(() => workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts." ||
            workspace.Baseline.HasBaseline, TimeSpan.FromMinutes(2), "the project did not open");
        if (!workspace.Baseline.HasBaseline)
        {
            workspace.Baseline.RefreshCommand.Execute(null);
            walkthrough.WaitUntil(() => workspace.Baseline.HasBaseline && !workspace.Baseline.RefreshCommand.IsRunning,
                TimeSpan.FromMinutes(10), "the Baseline was not captured");
        }

        walkthrough.WaitUntil(() => workspace.Context.Setup?.IsOpen == true,
            TimeSpan.FromMinutes(1), "first-open setup did not appear");
        workspace.Context.Setup!.SkipCommand.Execute(null);
        walkthrough.WaitUntil(() => !workspace.Context.Setup.IsOpen, TimeSpan.FromMinutes(1), "setup did not close");
        walkthrough.WaitUntil(() => workspace.PageModel<WarningsPageModel>().Grammar.HasChecked &&
            !workspace.PageModel<WarningsPageModel>().Grammar.CheckCommand.IsRunning,
            TimeSpan.FromMinutes(5), "the grammar check did not finish");

        walkthrough.WaitUntil(() => workspace.Selection.Texts.Count > 0, TimeSpan.FromMinutes(2), "the project has no Texts");
        foreach (var text in workspace.Selection.Texts.ToList())
        {
            text.IsChecked = true;
            var settle = DateTime.Now.AddMilliseconds(500);
            walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.PageModel<TextsPageModel>().Words.IsLoading,
                TimeSpan.FromMinutes(2), "the Texts' words did not load");
            if (workspace.PageModel<TextsPageModel>().Words.Rows.Count >= WordBudget) break;
        }
        output.WriteLine($"{workspace.Selection.Texts.Count(text => text.IsChecked)} texts, " +
            $"{workspace.PageModel<TextsPageModel>().Words.Rows.Count} words");

        var savedSelection = SelectionCommands.SetDefault(new SIL.Motif.Contract.Requests.SetDefaultSelectionRequest(
            walkthrough.ProjectPath, "Screenshot selection", workspace.Selection.ChosenTextIds,
            workspace.Selection.PastedWordEntries));
        Assert.True(savedSelection.Succeeded, savedSelection.Refusal?.Message);

        workspace.Assess.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(20), "the Assessment did not finish");
        Assert.True(workspace.Assess.State == RunState.Completed,
            $"The Assessment ended {workspace.Assess.State}: {workspace.Assess.Refusal?.Message}");
        var overview = await workspace.Context.Commands.OverviewAsync(
            new SIL.Motif.Contract.Requests.OverviewRequest(walkthrough.ProjectPath), CancellationToken.None);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.NotNull(overview.Value!.AssessmentId);
        walkthrough.WaitUntil(() => workspace.PageModel<OverviewPageModel>().Overview?.AssessmentId is not null,
            TimeSpan.FromMinutes(1), "the Overview did not load the stored Assessment");

        workspace.CurrentPage = WorkspacePage.Overview;
        Save(walkthrough.Window, Path.Combine(folder, "1-overview.png"));

        workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
        workspace.CurrentPage = WorkspacePage.Texts;
        // The first Text of a project is often a one-word placeholder, which teaches a reader nothing.
        var reader = workspace.PageModel<TextsPageModel>().ResultsInText;
        reader.SelectedText = reader.Texts.Skip(1).FirstOrDefault() ?? reader.Texts.FirstOrDefault();
        await AnalyzeTextsLayoutTests.SettleReaderAsync(workspace, walkthrough.Window);
        Save(walkthrough.Window, Path.Combine(folder, "2-texts-analyze.png"));

        workspace.CurrentPage = WorkspacePage.Warnings;
        Save(walkthrough.Window, Path.Combine(folder, "3-warnings.png"));

        // One harmless pending change in the scratch copy, so Review has something to list.
        var compare = workspace.Assess.Compare;
        workspace.Context.OpenTexts(TextsTab.Matrix);
        Dispatcher.UIThread.RunJobs();
        foreach (var preset in compare.Presets)
        {
            compare.SelectPresetCommand.Execute(preset);
            if (compare.Words.Any(word => word.ReadingChoices.Count > 0)) break;
        }
        output.WriteLine($"Staging from preset words: {compare.Words.Count}, with readings: " +
            $"{compare.Words.Count(word => word.ReadingChoices.Count > 0)}");
        // Fewest readings first: a word with one reading is the least ambiguous change to show.
        foreach (var candidate in compare.Words.Where(word => word.ReadingChoices.Count > 0 && word.Word.Length is > 5 and <= 12)
                     .OrderBy(word => word.ReadingChoices.Count).ThenBy(word => word.Word.Length).Take(300))
        {
            candidate.IsChecked = true;
            compare.ProposeCommand.Execute(ChangeKinds.AddCandidate);
            walkthrough.WaitUntil(() => !compare.ProposeCommand.IsRunning, TimeSpan.FromMinutes(2),
                "The candidate change did not finish");
            try { walkthrough.WaitUntil(() => compare.Changes.Count > 0, TimeSpan.FromSeconds(1), "no pending change"); }
            catch (Exception) { /* a word the change list skipped; the next one is tried */ }
            output.WriteLine($"Staged '{candidate.Word}': refusal {compare.Changes.LastRefusal?.Message ?? "none"}, skipped {compare.Changes.Snapshot.SkippedWord}, pending {compare.Changes.Count}");
            if (compare.Changes.Count > 0) break;
            candidate.IsChecked = false;
        }
        compare.ClearSelectionCommand.Execute(null);
        workspace.CurrentPage = WorkspacePage.Review;
        Save(walkthrough.Window, Path.Combine(folder, "4-review.png"));

        var rows = workspace.Assess.Words.Rows;
        var tried = rows.Where(row => !row.IsFailed && row.Word.Length <= 12).MaxBy(row => row.Word.Length) ??
            rows.FirstOrDefault(row => !row.IsFailed) ?? rows.First();
        workspace.Assess.OpenTryWord!(tried.Word);
        walkthrough.WaitUntil(() => !workspace.Assess.Trace.TryCommand.IsRunning, TimeSpan.FromMinutes(3),
            "Try a Word did not finish");
        output.WriteLine($"Tried '{tried.Word}': {workspace.Assess.Trace.AnswerText}");
        Save(walkthrough.Window, Path.Combine(folder, "5-try-a-word.png"));
    }

    private static void Save(MainWindow window, string path)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        // A layout defect on a real project is worth seeing, so it is written beside the picture, not thrown.
        try { LayoutAssertions.BeforeCapture(window); }
        catch (Exception exception)
        {
            File.AppendAllText(Path.ChangeExtension(path, ".layout.txt"), exception.Message + Environment.NewLine);
        }
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(from)) CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
