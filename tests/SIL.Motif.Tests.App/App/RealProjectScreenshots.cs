using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Tests.App;

/// <summary>Runs only when <c>MOTIF_SCREENSHOTS</c> and <c>MOTIF_SCREENSHOT_PROJECTS</c> both name folders.</summary>
public sealed class RealProjectScreenshotFactAttribute : FactAttribute
{
    public const string ProjectsVariable = "MOTIF_SCREENSHOT_PROJECTS";

    public RealProjectScreenshotFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ProjectsVariable)))
        {
            Skip = $"Set {ScreenshotFactAttribute.FolderVariable} and {ProjectsVariable} to capture real projects.";
        }
    }
}

/// <summary>
/// Walks each <c>.fwdata</c> in <c>MOTIF_SCREENSHOT_PROJECTS</c> through the real window, host and parser —
/// Baseline, grammar check, Texts, Assessment, Diagnostics and AI Handoff — and saves every page.
/// Each project is copied first, so the originals are never opened for writing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class RealProjectScreenshots(ITestOutputHelper output)
{
    private const int WordBudget = 150;

    [RealProjectScreenshotFact]
    public void CaptureEveryPageOfEachRealProject()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        var source = Environment.GetEnvironmentVariable(RealProjectScreenshotFactAttribute.ProjectsVariable)!;
        var only = Environment.GetEnvironmentVariable("MOTIF_SCREENSHOT_ONLY");
        var projects = Directory.GetFiles(source, "*.fwdata")
            .Where(path => string.IsNullOrWhiteSpace(only) ||
                only.Split(',').Contains(Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        Assert.NotEmpty(projects);

        foreach (var original in projects)
        {
            var name = Path.GetFileNameWithoutExtension(original);
            var started = DateTime.Now;
            try
            {
                Capture(original, Path.Combine(folder, name));
                output.WriteLine($"{name}: captured in {(DateTime.Now - started).TotalSeconds:F0}s");
            }
            catch (Exception exception)
            {
                Directory.CreateDirectory(Path.Combine(folder, name));
                File.WriteAllText(Path.Combine(folder, name, "failed.txt"), exception.ToString());
                output.WriteLine($"{name}: FAILED after {(DateTime.Now - started).TotalSeconds:F0}s — {exception.Message}");
                throw;
            }
        }
    }

    private void Capture(string original, string folder)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(original);
        var scratch = Path.Combine(Path.GetTempPath(), "SIL.Motif.RealScreens", Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(scratch, "projects", name);
        var managedRoot = Path.Combine(scratch, "managed");
        var handoffFolder = Path.Combine(scratch, "handoff");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(managedRoot);
        Directory.CreateDirectory(handoffFolder);
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
                walkthrough.Window.Width = 1240;
                walkthrough.Window.Height = 780;
                walkthrough.Show();
                await Drive(walkthrough, original, folder);
                SaveEveryPage(walkthrough, folder);
                SaveTimingParserWorkLimit(walkthrough, folder);
                SaveRecordedWarnings(walkthrough, folder);
                SaveInTextWithNoTextChecked(walkthrough, folder);
            }, TimeSpan.FromMinutes(30));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(scratch);
        }
    }

    private async Task Drive(WalkthroughWindow walkthrough, string original, string folder)
    {
        var workspace = walkthrough.Workspace;
        await workspace.SetProjectAsync(walkthrough.ProjectPath);
        walkthrough.WaitUntil(() => workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts." ||
            workspace.Baseline.HasBaseline, TimeSpan.FromMinutes(2), "the project did not open");

        workspace.Baseline.RefreshCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Baseline.HasBaseline && !workspace.Baseline.RefreshCommand.IsRunning,
            TimeSpan.FromMinutes(10), "the Baseline was not captured");

        walkthrough.WaitUntil(() => workspace.Context.Setup?.IsOpen == true,
            TimeSpan.FromMinutes(1), "first-open setup did not appear");
        var setup = workspace.Context.Setup!;
        CaptureSetupStep(walkthrough, folder, "project");
        setup.NextCommand.Execute(null);
        walkthrough.WaitUntil(() => setup.Step == 1, TimeSpan.FromSeconds(1), "setup did not advance to Text selection");

        walkthrough.WaitUntil(() => workspace.Context.GrammarCheck.HasChecked && !workspace.Context.GrammarCheck.CheckCommand.IsRunning,
            TimeSpan.FromMinutes(5), "the grammar check did not finish");

        walkthrough.WaitUntil(() => workspace.Selection.Texts.Count > 0 ||
            workspace.Selection.TextsEmptyMessage == "This Baseline has no Texts.", TimeSpan.FromMinutes(2), "the Texts did not load");
        if (workspace.Selection.Texts.FirstOrDefault() is { } text)
        {
            text.IsChecked = true;
            walkthrough.WaitUntil(() => !workspace.PageModel<TextsPageModel>().Words.IsLoading,
                TimeSpan.FromMinutes(2), "the setup word preview did not finish loading");
        }
        else workspace.Selection.PastedWords = "setup sample";
        CaptureSetupStep(walkthrough, folder, "selection");
        setup.NextCommand.Execute(null);
        walkthrough.WaitUntil(() => setup.Step == 2, TimeSpan.FromSeconds(1), $"setup did not advance to its limits (step {setup.Step})");
        CaptureSetupStep(walkthrough, folder, "limits");
        setup.NextCommand.Execute(null);
        walkthrough.WaitUntil(() => setup.Step == 3, TimeSpan.FromSeconds(1), "setup did not advance to its first run");
        CaptureSetupStep(walkthrough, folder, "first-run");
        setup.SkipCommand.Execute(null);
        walkthrough.WaitUntil(() => !setup.IsOpen, TimeSpan.FromMinutes(1), "setup did not close after Skip");

        // Texts first, so In text has something to show; the sample's word list when the project has none.
        foreach (var textChoice in workspace.Selection.Texts.ToList())
        {
            textChoice.IsChecked = true;
            var settle = DateTime.Now.AddMilliseconds(500);
            walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.PageModel<TextsPageModel>().Words.IsLoading,
                TimeSpan.FromMinutes(2), "the Texts' words did not load");
            if (workspace.PageModel<TextsPageModel>().Words.Rows.Count >= WordBudget) break;
        }
        if (workspace.Selection.Texts.Count == 0)
        {
            var list = Path.ChangeExtension(original, null) + "-words.txt";
            if (File.Exists(list))
                workspace.Selection.PastedWords = string.Join(Environment.NewLine, File.ReadLines(list)
                    .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).Take(WordBudget));
        }
        output.WriteLine($"{Path.GetFileName(original)}: {workspace.Selection.Texts.Count(text => text.IsChecked)} texts, " +
            $"{workspace.PageModel<TextsPageModel>().Words.Rows.Count} text words, {workspace.Selection.PastedWordEntries.Count} pasted words, " +
            $"{workspace.Context.GrammarCheck.Warnings.TotalCount} grammar findings");

        var savedSelection = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            walkthrough.ProjectPath, "Screenshot selection", workspace.Selection.ChosenTextIds,
            workspace.Selection.PastedWordEntries));
        Assert.True(savedSelection.Succeeded, savedSelection.Refusal?.Message);

        workspace.Assess.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(20), "the Assessment did not finish");
        Assert.True(workspace.Assess.State == RunState.Completed,
            $"The Assessment ended {workspace.Assess.State}: {workspace.Assess.Refusal?.Message}");
        var overview = await workspace.Context.Commands.OverviewAsync(
            new OverviewRequest(walkthrough.ProjectPath), CancellationToken.None);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.NotNull(overview.Value!.AssessmentId);
        walkthrough.WaitUntil(() => workspace.Context.Health.Overview?.AssessmentId is not null,
            TimeSpan.FromMinutes(1), "the Overview did not load the stored Assessment");

        var tokens = SelectionModelFixture.VisibleLines(workspace.PageModel<TextsPageModel>().ResultsInText).SelectMany(line => line.Tokens).Where(token => token.IsWord).ToList();
        var inText = tokens.FirstOrDefault(token => token.Verdict == OccurrenceVerdict.Differs) ?? tokens.FirstOrDefault();
        if (inText is not null) workspace.PageModel<TextsPageModel>().ResultsInText.SelectToken(inText);

        workspace.PageModel<SettingsPageModel>().Handoff.Handoff.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.PageModel<SettingsPageModel>().Handoff.Handoff.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(5), "the Handoff did not finish");

    }

    private static void CaptureSetupStep(WalkthroughWindow walkthrough, string folder, string step)
    {
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Save(walkthrough.Window, Path.Combine(folder, $"setup-{step}-{theme}.png"));
        }
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    private static void SaveEveryPage(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        try
        {
            workspace.Context.Diagnostics = true;
            foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (var (name, page, _) in PageScreenshots.Views())
                {
                    workspace.CurrentPage = page;
                    Save(walkthrough.Window, Path.Combine(folder, $"{name}-{theme}.png"));
                }

                // A narrow window: the sidebar collapses to icons, and the badges stay.
                walkthrough.Window.Width = 960;
                foreach (var (name, page, _) in PageScreenshots.Views().Take(2))
                {
                    workspace.CurrentPage = page;
                    Save(walkthrough.Window, Path.Combine(folder, $"{name}-narrow-{theme}.png"));
                }
                walkthrough.Window.Width = 1240;
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    private static void SaveTimingParserWorkLimit(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        var word = workspace.Assess.Result!.Words.FirstOrDefault(row => row.Outcome is "analysed" or "no-analysis")
            ?? workspace.Assess.Result.Words.First();
        _ = workspace.Assess.RerunAsync([word.Word], 60_000, new ParserWorkLimit(1));
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Refused &&
            !workspace.Assess.IsActive, TimeSpan.FromMinutes(2), "The parser-work-limited re-run did not finish");
        Assert.Equal(RunState.Completed, workspace.Assess.State);
        workspace.Context.Diagnostics = true;
        workspace.Context.OpenDiagnostics(DiagnosticsTab.SpeedByRule);
        walkthrough.WaitUntil(() => workspace.Context.EvidencePublication.IsCompleted,
            TimeSpan.FromMinutes(2), "The replacement evidence did not finish loading");
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Save(walkthrough.Window, Path.Combine(folder, $"diagnostics-rule-work-after-rerun-{theme}.png"));
        }
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    private static void SaveRecordedWarnings(WalkthroughWindow walkthrough, string folder)
    {
        var warnings = walkthrough.Workspace.Context.GrammarCheck.Warnings;
        var row = warnings.Rows.Cast<GrammarWarningRowViewModel>().MaxBy(finding => finding.RepeatCount);
        if (row is null) return;
        walkthrough.Workspace.Context.Diagnostics = true;
        walkthrough.Workspace.Context.OpenDiagnostics(DiagnosticsTab.Warnings);
        Save(walkthrough.Window, Path.Combine(folder, "5b-warnings-kind-light.png"));
    }

    // The empty state a person meets after unchecking every Text, which a run over Texts never shows.
    private static void SaveInTextWithNoTextChecked(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        foreach (var text in workspace.Selection.Texts) text.IsChecked = false;
        var settle = DateTime.Now.AddMilliseconds(500);
        walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.PageModel<TextsPageModel>().Words.IsLoading,
            TimeSpan.FromMinutes(1), "unchecking the Texts did not settle");
        workspace.Context.OpenPage(WorkspacePage.Texts);
        Save(walkthrough.Window, Path.Combine(folder, "2b-texts-analyze-no-text-light.png"));
    }

    private static void Save(MainWindow window, string path)
    {
        PageScreenshots.Settle(window);
        LayoutAssertions.BeforeCapture(window);
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
