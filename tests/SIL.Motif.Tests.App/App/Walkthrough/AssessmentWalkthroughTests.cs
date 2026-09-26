using System.Diagnostics;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AssessmentWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void TimingSourceButtonsExplainOrDisableEmptyInputs()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: FakeParser.ExecutablePath);
            walkthrough.Window.Height = 1100;
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);

            var result = walkthrough.Workspace.Assess.Result;
            Assert.NotNull(result);
            Assert.NotEmpty(result!.Words);
            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.Click("Open accuracy in Texts");
            Assert.Equal(WorkspacePage.Texts, walkthrough.Workspace.CurrentPage);
            Assert.Equal(TextsTab.Matrix, walkthrough.Workspace.PageModel<TextsPageModel>().Tab);
            Assert.Contains(walkthrough.Workspace.Assess.Compare.Cells,
                cell => cell.Row == WordProjectStatus.Approved && cell.IsSelected);

            walkthrough.ShowPage(WorkspacePage.Timing);
            var timing = walkthrough.Workspace.PageModel<TimingPageModel>();
            Button SourceButton(string name) => walkthrough.Window.GetLogicalDescendants().OfType<Button>()
                .Single(button => Equals(button.Content, name));
            var sources = new[]
            {
                ("Use cell", "Choose a matrix cell from Texts first."),
                ("Use list", "Choose a word list from Texts first."),
                ("Pick words", "Enter one or more words, one per line."),
                ("Chosen in Texts", "Tick words in Texts first."),
            };
            foreach (var (name, reason) in sources)
            {
                var button = SourceButton(name);
                Assert.False(button.IsEffectivelyEnabled);
                Assert.Equal(reason, AutomationProperties.GetHelpText(button));
                Assert.Contains(walkthrough.Window.GetVisualDescendants().OfType<TextBlock>(), text =>
                    text.IsEffectivelyVisible && text.Text == reason);
            }

            walkthrough.Type("Words picked by hand", $"   {Environment.NewLine}   ");
            Assert.False(SourceButton("Pick words").IsEffectivelyEnabled);
            Assert.Equal("Enter one or more words, one per line.",
                AutomationProperties.GetHelpText(SourceButton("Pick words")));

            var matrixPicker = walkthrough.Find<ComboBox>("Matrix cell from Texts");
            HeadlessClick.Click(walkthrough.Window, matrixPicker, "Matrix cell from Texts");
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            Assert.NotNull(timing.SelectedMatrixCell);
            Assert.True(SourceButton("Use cell").IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(SourceButton("Use cell"))));
            walkthrough.Click("Use cell");
            walkthrough.WaitUntil(() => !timing.UseMatrixCellCommand.IsRunning && timing.KindTiming is not null,
                WalkthroughSteps.Remaining(deadline), "Timing did not load the chosen matrix cell");

            var listPicker = walkthrough.Find<ComboBox>("List from Texts");
            HeadlessClick.Click(walkthrough.Window, listPicker, "List from Texts");
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            Assert.NotNull(timing.SelectedTextsList);
            Assert.True(SourceButton("Use list").IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(SourceButton("Use list"))));
            walkthrough.Click("Use list");
            walkthrough.WaitUntil(() => !timing.UseTextsListCommand.IsRunning && timing.KindTiming is not null,
                WalkthroughSteps.Remaining(deadline), "Timing did not load the chosen word list");

            var oldScope = timing.ScopeLabel;
            walkthrough.Type("Words picked by hand", string.Join(Environment.NewLine,
                result.Words.Select(assessed => assessed.Word)));
            Assert.True(SourceButton("Pick words").IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(SourceButton("Pick words"))));
            walkthrough.Click("Pick words");
            walkthrough.WaitUntil(() => !timing.UsePickedWordsCommand.IsRunning && timing.KindTiming is not null,
                WalkthroughSteps.Remaining(deadline), "Timing did not load the picked word");
            Assert.NotEqual(oldScope, timing.ScopeLabel);

            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.Click("Open Text Coverage in Texts");
            var comparedWord = walkthrough.Workspace.Assess.Compare.Words.First().Word;
            walkthrough.ShowTextsTab(TextsTab.Matrix);
            var tick = walkthrough.Find<CheckBox>($"Tick {comparedWord} for a change");
            HeadlessClick.Click(walkthrough.Window, tick, $"Tick {comparedWord} for a change");
            Assert.True(tick.IsChecked);
            walkthrough.ShowPage(WorkspacePage.Timing);
            Assert.True(SourceButton("Chosen in Texts").IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(SourceButton("Chosen in Texts"))));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [RealParserFact]
    public void RunningAssessmentRendersReadingsAndPublishesStatistics()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            var baselineDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, baselineDeadline);

            var assessmentDeadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, assessmentDeadline);
            Assert.True(walkthrough.Find<Button>("Write the AI Handoff folder").IsEffectivelyEnabled);

            var result = walkthrough.Workspace.Assess.Result;
            Assert.NotNull(result);
            Assert.StartsWith("3 searches completed; 0 incomplete", result!.SummaryMarkdown);
            Assert.Equal(3, result.Words.Count);
            Assert.Equal(2, result.Words.Count(word => word.Outcome == "analysed"));
            Assert.Equal(1, result.Words.Count(word => word.Outcome == "no-analysis"));
            Assert.All(result.Words, word => Assert.False(word.IsIncomplete));

            walkthrough.Window.ApplyTemplate();
            walkthrough.Window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            var comparePanel = Assert.Single(walkthrough.Window.GetLogicalDescendants().OfType<ComparePanel>());
            Assert.Same(walkthrough.Workspace.Assess.Compare, comparePanel.Compare);
            Assert.Equal(result.Words.Select(word => word.Word).Order(StringComparer.Ordinal),
                walkthrough.Workspace.Assess.Compare.Words.Select(word => word.Word).Order(StringComparer.Ordinal));
            var rows = walkthrough.Workspace.Assess.Words.Rows.Cast<AssessWordRowViewModel>().ToList();
            Assert.Equal(3, rows.Count);
            // Readings are resolved against the project: every parsed word's morphs name a form, never an identifier.
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.All(row.Readings.SelectMany(r => r.Morphs),
                morph => Assert.DoesNotContain("(missing", morph.Form, StringComparison.Ordinal)));
            Assert.All(rows.Where(row => row.IsParsed), row => Assert.NotEmpty(row.Readings));
            Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);

            Assert.True(walkthrough.Workspace.Context.HasEvidence);
            Assert.True(walkthrough.Named<ContentControl>("StatisticsHost").IsVisible);
            Assert.NotNull(walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.AssessmentId);
            Assert.Equal(walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.Groups[0],
                walkthrough.Workspace.PageModel<TimingPageModel>().Statistics.SelectedGroup);

            var details = walkthrough.Window.GetLogicalDescendants().OfType<Expander>()
                .Single(expander => Equals(expander.Header, "Detailed statistics"));
            details.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            walkthrough.Click("Refresh statistics");
            var statisticsDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
            var statistics = walkthrough.Workspace.PageModel<TimingPageModel>().Statistics;
            try
            {
                walkthrough.WaitUntil(
                    () => statistics.Rows.Count > 0 && statistics.LoadCommand.ExecutionTask is not { IsCompleted: false },
                    WalkthroughSteps.Remaining(statisticsDeadline), "statistics did not load any rows");
            }
            catch (Xunit.Sdk.XunitException failure)
            {
                Assert.Fail(failure.Message + "; " + StatisticsState(statistics));
            }

            Assert.True(walkthrough.Workspace.Baseline.HasAssessment);
            var projectLocator = new ProjectLocator(
                Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
            using var database = MotifDatabase.OpenOwned(
                ProjectDatabaseCatalog.DatabasePathFor(projectLocator), projectLocator,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var invocations = new RetainedInvocationRepository(database).List(
                ProjectWorkspaceKey.Compute(projectLocator));
            Assert.Single(invocations);
            Assert.Equal(result.InvocationId, walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.InvocationId);
            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    // Read when the wait gives up, so a failure tells a refusal from a load that never finished.
    private static string StatisticsState(StatisticsViewModel statistics) =>
        $"statistics refusal code='{statistics.Refusal?.Code}', " +
        $"statistics refusal='{statistics.Refusal?.Message}', stale='{statistics.IsStale}', " +
        $"fetched rows='{statistics.RowCount}', visible rows='{statistics.Rows.Count}', " +
        $"load running='{statistics.LoadCommand.IsRunning}', " +
        $"load task='{statistics.LoadCommand.ExecutionTask?.Status.ToString() ?? "never started"}'";
}
