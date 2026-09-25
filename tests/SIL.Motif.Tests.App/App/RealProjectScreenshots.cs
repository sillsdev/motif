using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;
using SIL.Motif.Commands.Queries;
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
/// Baseline, grammar check, Texts, Assessment, Try a Word, Timing and AI Handoff — and saves every page.
/// Each project is copied first, so the originals are never opened for writing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
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
                SaveTimingStepLimit(walkthrough, folder);
                SaveGrammarWithAKindChosen(walkthrough, folder);
                SaveCompareWithCellsChosen(walkthrough, folder);
                SaveWhatChangedAfterARerun(walkthrough, folder);
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

        walkthrough.WaitUntil(() => workspace.PageModel<WarningsPageModel>().Grammar.HasChecked && !workspace.PageModel<WarningsPageModel>().Grammar.CheckCommand.IsRunning,
            TimeSpan.FromMinutes(5), "the grammar check did not finish");

        walkthrough.WaitUntil(() => workspace.Selection.Texts.Count > 0 ||
            workspace.Selection.TextsEmptyMessage == "This Baseline has no Texts.", TimeSpan.FromMinutes(2), "the Texts did not load");
        if (workspace.Selection.Texts.FirstOrDefault() is { } text) text.IsChecked = true;
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
            $"{workspace.PageModel<WarningsPageModel>().Grammar.Warnings.TotalCount} grammar findings");

        workspace.Assess.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(20), "the Assessment did not finish");
        Assert.True(workspace.Assess.State == RunState.Completed,
            $"The Assessment ended {workspace.Assess.State}: {workspace.Assess.Refusal?.Message}");

        workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.Execute(null);
        walkthrough.WaitUntil(() => !workspace.PageModel<TimingPageModel>().Statistics.LoadCommand.IsRunning, TimeSpan.FromMinutes(2), "Statistics did not load");

        var tokens = workspace.PageModel<TextsPageModel>().ResultsInText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToList();
        var inText = tokens.FirstOrDefault(token => token.Verdict == OccurrenceVerdict.Differs) ?? tokens.FirstOrDefault();
        if (inText is not null) workspace.PageModel<TextsPageModel>().ResultsInText.SelectToken(inText);

        var compare = workspace.Assess.Compare;
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.Violation));
        if (compare.Words.FirstOrDefault() is { } spelling)
        {
            spelling.IsChecked = true;
            compare.ProposeCommand.Execute(ChangeKinds.IncorrectSpelling);
            walkthrough.WaitUntil(() => !compare.ProposeCommand.IsRunning,
                TimeSpan.FromMinutes(2), "The spelling change did not finish");
        }
        compare.ClearSelectionCommand.Execute(null);
        var reading = compare.Words.FirstOrDefault(word => word.ReadingChoices.Count > 0);
        if (reading is not null)
        {
            reading.SelectedReading = reading.ReadingChoices[0];
            reading.IsChecked = true;
            compare.ProposeCommand.Execute(ChangeKinds.Approve);
            walkthrough.WaitUntil(() => !compare.ProposeCommand.IsRunning,
                TimeSpan.FromMinutes(2), "The analysis change did not finish");
        }
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.New));
        if (compare.Words.FirstOrDefault(word => word.ReadingChoices.Count > 0 && word.Word != reading?.Word)
            is { } candidate)
        {
            candidate.IsChecked = true;
            compare.ProposeCommand.Execute(ChangeKinds.AddCandidate);
            walkthrough.WaitUntil(() => !compare.ProposeCommand.IsRunning,
                TimeSpan.FromMinutes(2), "The candidate change did not finish");
        }
        compare.ClearSelectionCommand.Execute(null);

        workspace.PageModel<AiHandoffPageModel>().Handoff.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.PageModel<AiHandoffPageModel>().Handoff.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(5), "the Handoff did not finish");

        // Try a Word on a word that did not parse, since that is where its failure story shows.
        var rows = workspace.Assess.Words.Rows;
        var tried = rows.FirstOrDefault(row => row.IsFailed && row.MissedApproved.Count > 0)
            ?? rows.FirstOrDefault(row => row.IsFailed) ?? rows.FirstOrDefault();
        if (tried is not null)
        {
            // Last, the way a person gets there, so no later choice of word clears the trace on screen.
            workspace.Assess.OpenTryWord!(tried.Word);
            walkthrough.WaitUntil(() => !workspace.Assess.Trace.TryCommand.IsRunning, TimeSpan.FromMinutes(3), "Try a Word did not finish");
            output.WriteLine($"Tried '{tried.Word}': {workspace.Assess.Trace.AnswerText}");
        }
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
            foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (var (name, page, tab) in PageScreenshots.Views())
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
                    workspace.CurrentPage = page;
                    Save(walkthrough.Window, Path.Combine(folder, $"{name}-{theme}.png"));
                }

                // A narrow window: the sidebar collapses to icons, and the badges stay.
                walkthrough.Window.Width = 960;
                foreach (var (name, page, tab) in PageScreenshots.Views().Take(2))
                {
                    workspace.PageModel<TextsPageModel>().Tab = tab;
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

    private static void SaveTimingStepLimit(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        var timing = workspace.PageModel<TimingPageModel>();
        var word = workspace.Assess.Result!.Words.FirstOrDefault(row => row.Outcome is "analysed" or "no-analysis")
            ?? workspace.Assess.Result.Words.First();
        _ = workspace.Assess.RerunAsync([word.Word], 60_000, new StepCap(1));
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Refused &&
            !workspace.Assess.IsActive, TimeSpan.FromMinutes(2), "The step-limited re-run did not finish");
        Assert.Equal(RunState.Completed, workspace.Assess.State);
        timing.SelectWordSetCommand.Execute("step-limit");
        walkthrough.WaitUntil(() => !timing.SelectWordSetCommand.IsRunning && timing.HasTiming,
            TimeSpan.FromMinutes(2), "Step-limit timing did not load");
        Assert.True(timing.HasSelectedWords && timing.KindTiming!.Words.Any(word => word.Completion == "Step limit"),
            "The step-limit capture must show a recorded step-limited word.");
        walkthrough.Window.Height = 1650;
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            walkthrough.Workspace.CurrentPage = WorkspacePage.Timing;
            Save(walkthrough.Window, Path.Combine(folder, $"4b-timing-step-limit-{theme}.png"));
        }
        walkthrough.Window.Height = 780;
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    // The matrix filtering the list: the violations preset, then the single largest cell.
    private static void SaveCompareWithCellsChosen(WalkthroughWindow walkthrough, string folder)
    {
        var compare = walkthrough.Workspace.Assess.Compare;
        walkthrough.Workspace.Context.OpenTexts(TextsTab.Matrix);
        compare.SelectPresetCommand.Execute(compare.Presets.Single(preset => preset.Family == CompareFamilyKind.Violation));
        Save(walkthrough.Window, Path.Combine(folder, "2f-texts-matrix-violations-light.png"));
        compare.Toggle(compare.Cells.MaxBy(cell => cell.Count)!, additive: false);
        Save(walkthrough.Window, Path.Combine(folder, "2g-texts-matrix-largest-cell-light.png"));
        walkthrough.Workspace.PageModel<TextsPageModel>().Tab = TextsTab.Words;
        Save(walkthrough.Window, Path.Combine(folder, "2h-texts-words-mini-matrix-light.png"));
        compare.ClearSelectionCommand.Execute(null);
    }

    // A real re-run of a few unknown words with more time, so "What changed" shows moves the parser made.
    private static void SaveWhatChangedAfterARerun(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        var words = workspace.Assess.Compare.RerunWords.Take(RerunBudget).ToArray();
        if (words.Length == 0) return;
        _ = workspace.Assess.RerunAsync(words, RerunLimitMs);
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused &&
            !workspace.Assess.IsActive, TimeSpan.FromMinutes(20), "the re-run did not finish");
        Assert.Equal(RunState.Completed, workspace.Assess.State);
        foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            Application.Current!.RequestedThemeVariant = variant;
            workspace.Context.OpenTexts(TextsTab.WhatChanged);
            Save(walkthrough.Window, Path.Combine(folder, $"2b-texts-what-changed-after-rerun-{theme}.png"));
        }
        Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
    }

    // Few enough words, with enough time each, that the re-run finishes in minutes and still moves some.
    private const int RerunBudget = 12;
    private const int RerunLimitMs = 20_000;

    // The largest kind of finding chosen, so its own view is reviewed as well as the whole list.
    private static void SaveGrammarWithAKindChosen(WalkthroughWindow walkthrough, string folder)
    {
        var warnings = walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar.Warnings;
        var kind = warnings.WarningGroups.Concat(warnings.InformationGroups).MaxBy(group => group.Count);
        if (kind is null) return;
        warnings.SelectGroupCommand.Execute(kind);
        walkthrough.Workspace.CurrentPage = WorkspacePage.Warnings;
        Save(walkthrough.Window, Path.Combine(folder, "5b-warnings-kind-light.png"));
        warnings.SelectGroupCommand.Execute(kind);
    }

    // The empty state a person meets after unchecking every Text, which a run over Texts never shows.
    private static void SaveInTextWithNoTextChecked(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        foreach (var text in workspace.Selection.Texts) text.IsChecked = false;
        var settle = DateTime.Now.AddMilliseconds(500);
        walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.PageModel<TextsPageModel>().Words.IsLoading,
            TimeSpan.FromMinutes(1), "unchecking the Texts did not settle");
        workspace.Context.OpenTexts(TextsTab.InText);
        Save(walkthrough.Window, Path.Combine(folder, "2i-texts-in-text-no-text-light.png"));
    }

    private static void Save(MainWindow window, string path)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path);
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(from)) CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }
}
