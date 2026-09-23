using Avalonia;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using Xunit.Abstractions;

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
/// Baseline, grammar check, Texts, Assessment, Try a Word, Statistics and Handoff — and saves every stage.
/// Each project is copied first, so the originals are never opened for writing.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class RealProjectScreenshots(ITestOutputHelper output)
{
    private const int WordBudget = 150;

    [RealProjectScreenshotFact]
    public void CaptureEveryStageOfEachRealProject()
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
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var walkthrough = new WalkthroughWindow(managedRoot, fwDataPath, handoffFolder);
                walkthrough.Window.Width = 1240;
                walkthrough.Window.Height = 780;
                walkthrough.Show();
                Drive(walkthrough, original);
                SaveEveryStage(walkthrough, folder);
                SaveGrammarWithAKindChosen(walkthrough, folder);
                SaveInTextWithNoTextChecked(walkthrough, folder);
                return Task.CompletedTask;
            }, TimeSpan.FromMinutes(30));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(scratch);
        }
    }

    private void Drive(WalkthroughWindow walkthrough, string original)
    {
        var workspace = walkthrough.Workspace;
        walkthrough.Click("Browse for a FieldWorks project file");
        walkthrough.WaitUntil(() => workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts." ||
            workspace.Baseline.HasBaseline, TimeSpan.FromMinutes(2), "the project did not open");

        workspace.Baseline.RefreshCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Baseline.HasBaseline && !workspace.Baseline.RefreshCommand.IsRunning,
            TimeSpan.FromMinutes(10), "the Baseline was not captured");
        walkthrough.WaitUntil(() => workspace.Grammar.HasChecked && !workspace.Grammar.CheckCommand.IsRunning,
            TimeSpan.FromMinutes(5), "the grammar check did not finish");

        walkthrough.WaitUntil(() => workspace.Selection.Texts.Count > 0 ||
            workspace.Selection.TextsEmptyMessage == "This Baseline has no Texts.", TimeSpan.FromMinutes(2), "the Texts did not load");

        // Texts first, so In text has something to show; the sample's word list when the project has none.
        foreach (var text in workspace.Selection.Texts.ToList())
        {
            text.IsChecked = true;
            var settle = DateTime.Now.AddMilliseconds(500);
            walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.Words.IsLoading,
                TimeSpan.FromMinutes(2), "the Texts' words did not load");
            if (workspace.Words.Rows.Count >= WordBudget) break;
        }
        if (workspace.Selection.Texts.Count == 0)
        {
            var list = Path.ChangeExtension(original, null) + "-words.txt";
            if (File.Exists(list))
                workspace.Selection.PastedWords = string.Join(Environment.NewLine, File.ReadLines(list)
                    .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#')).Take(WordBudget));
        }
        output.WriteLine($"{Path.GetFileName(original)}: {workspace.Selection.Texts.Count(text => text.IsChecked)} texts, " +
            $"{workspace.Words.Rows.Count} text words, {workspace.Selection.PastedWordEntries.Count} pasted words, " +
            $"{workspace.Grammar.Warnings.TotalCount} grammar findings");

        workspace.Assess.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(20), "the Assessment did not finish");
        Assert.Equal(RunState.Completed, workspace.Assess.State);

        workspace.Statistics.LoadCommand.Execute(null);
        walkthrough.WaitUntil(() => !workspace.Statistics.LoadCommand.IsRunning, TimeSpan.FromMinutes(2), "Statistics did not load");

        // Try a Word on a word that did not parse, since that is where its failure story shows.
        var rows = workspace.Assess.Words.Rows;
        var tried = rows.FirstOrDefault(row => row.IsFailed && row.MissedApproved.Count > 0)
            ?? rows.FirstOrDefault(row => row.IsFailed) ?? rows.FirstOrDefault();
        if (tried is not null)
        {
            workspace.Assess.Words.SelectedRow = tried;
            workspace.Assess.Trace.TryCommand.Execute(null);
            walkthrough.WaitUntil(() => !workspace.Assess.Trace.TryCommand.IsRunning, TimeSpan.FromMinutes(3), "Try a Word did not finish");
            output.WriteLine($"Tried '{tried.Word}': {workspace.Assess.Trace.AnswerText}");
        }

        var tokens = workspace.ResultsInText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToList();
        var inText = tokens.FirstOrDefault(token => token.Verdict == OccurrenceVerdict.Differs) ?? tokens.FirstOrDefault();
        if (inText is not null) workspace.ResultsInText.SelectToken(inText);

        workspace.Handoff.RunCommand.Execute(null);
        walkthrough.WaitUntil(() => workspace.Handoff.State is RunState.Completed or RunState.Cancelled or RunState.Refused,
            TimeSpan.FromMinutes(5), "the Handoff did not finish");
    }

    private static void SaveEveryStage(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        try
        {
            foreach (var (theme, variant) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
            {
                Application.Current!.RequestedThemeVariant = variant;
                foreach (var (name, stage, view) in new[]
                {
                    ("1-project", WorkflowStage.Project, ResultsView.Words),
                    ("2-grammar", WorkflowStage.Grammar, ResultsView.Words),
                    ("3-texts", WorkflowStage.Texts, ResultsView.Words),
                    ("4-results-words", WorkflowStage.Results, ResultsView.Words),
                    ("5-results-intext", WorkflowStage.Results, ResultsView.InText),
                    ("6-results-statistics", WorkflowStage.Results, ResultsView.Statistics),
                    ("7-handoff", WorkflowStage.Handoff, ResultsView.Words),
                })
                {
                    workspace.CurrentStage = stage;
                    workspace.ResultsView = view;
                    Save(walkthrough.Window, Path.Combine(folder, $"{name}-{theme}.png"));
                }
            }
        }
        finally
        {
            Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
        }
    }

    // The largest kind of finding chosen, so its own view is reviewed as well as the whole list.
    private static void SaveGrammarWithAKindChosen(WalkthroughWindow walkthrough, string folder)
    {
        var warnings = walkthrough.Workspace.Grammar.Warnings;
        var kind = warnings.LeftOutGroups.Concat(warnings.WorthALookGroups).MaxBy(group => group.Count);
        if (kind is null) return;
        warnings.SelectGroupCommand.Execute(kind);
        walkthrough.Workspace.CurrentStage = WorkflowStage.Grammar;
        Save(walkthrough.Window, Path.Combine(folder, "2b-grammar-kind-light.png"));
        warnings.SelectGroupCommand.Execute(kind);
    }

    // The empty state a person meets after unchecking every Text, which a run over Texts never shows.
    private static void SaveInTextWithNoTextChecked(WalkthroughWindow walkthrough, string folder)
    {
        var workspace = walkthrough.Workspace;
        foreach (var text in workspace.Selection.Texts) text.IsChecked = false;
        var settle = DateTime.Now.AddMilliseconds(500);
        walkthrough.WaitUntil(() => DateTime.Now > settle && !workspace.Words.IsLoading,
            TimeSpan.FromMinutes(1), "unchecking the Texts did not settle");
        workspace.CurrentStage = WorkflowStage.Results;
        workspace.ResultsView = ResultsView.InText;
        Save(walkthrough.Window, Path.Combine(folder, "8-results-intext-no-text-light.png"));
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
