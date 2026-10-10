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
    public void OverviewAccuracyOpensTextsAndTimingSourcesExplainEmptyInputs()
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
            walkthrough.Click("Open Approved analyses kept in the Matrix");
            Assert.Equal(WorkspacePage.Texts, walkthrough.Workspace.CurrentPage);
            Assert.Equal(TextsTab.Matrix, walkthrough.Workspace.PageModel<TextsPageModel>().Tab);
            Assert.Contains(walkthrough.Workspace.Assess.Compare.Cells,
                cell => cell.Row == WordProjectStatus.Approved && cell.IsSelected);

            walkthrough.ShowPage(WorkspacePage.Timing);
            var timing = walkthrough.Workspace.PageModel<TimingPageModel>();
            var sourceCommandTimeout = TimeSpan.FromSeconds(20);
            Control OpenMenu(string openerName)
            {
                var opener = walkthrough.Find<Button>(openerName);
                var flyout = Assert.IsType<Flyout>(opener.Flyout);
                flyout.ShowAt(opener);
                PageScreenshots.Settle(walkthrough.Window);
                return Assert.IsAssignableFrom<Control>(flyout.Content);
            }

            Button MenuButton(Control menu, string name) => menu.GetLogicalDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetName(button) == name);

            void ClickMenu(Control menu, string name)
            {
                var button = MenuButton(menu, name);
                HeadlessClick.Click(TopLevel.GetTopLevel(button)!, button, name);
            }

            var matrixMenu = OpenMenu("Words from a Matrix cell");
            var useCell = MenuButton(matrixMenu, "Use cell");
            Assert.False(useCell.IsEffectivelyEnabled);
            Assert.Equal("Choose a matrix cell above first.", AutomationProperties.GetHelpText(useCell));
            var matrixPicker = matrixMenu.GetLogicalDescendants().OfType<ComboBox>()
                .Single(combo => AutomationProperties.GetName(combo) == "Matrix cell from Texts");
            HeadlessClick.Click(TopLevel.GetTopLevel(matrixPicker)!, matrixPicker, "Matrix cell from Texts");
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            Assert.NotNull(timing.SelectedMatrixCell);
            Assert.True(useCell.IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(useCell)));
            ClickMenu(matrixMenu, "Use cell");
            Assert.False(walkthrough.Find<Button>("Words from a Matrix cell").Flyout!.IsOpen);
            walkthrough.WaitUntil(() => !timing.UseMatrixCellCommand.IsRunning && timing.KindTiming is not null,
                sourceCommandTimeout, "Timing did not load the chosen matrix cell");

            var listMenu = OpenMenu("Words from a list");
            var useList = MenuButton(listMenu, "Use list");
            Assert.False(useList.IsEffectivelyEnabled);
            Assert.Equal("Choose a word list above first.", AutomationProperties.GetHelpText(useList));
            var listPicker = listMenu.GetLogicalDescendants().OfType<ComboBox>()
                .Single(combo => AutomationProperties.GetName(combo) == "List from Texts");
            HeadlessClick.Click(TopLevel.GetTopLevel(listPicker)!, listPicker, "List from Texts");
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            walkthrough.Window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None,
                Avalonia.Input.PhysicalKey.None, null);
            Assert.NotNull(timing.SelectedTextsList);
            Assert.True(useList.IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(useList)));
            ClickMenu(listMenu, "Use list");
            Assert.False(walkthrough.Find<Button>("Words from a list").Flyout!.IsOpen);
            walkthrough.WaitUntil(() => !timing.UseTextsListCommand.IsRunning && timing.KindTiming is not null,
                sourceCommandTimeout, "Timing did not load the chosen word list");

            var pastedWords = result.Words.Select(assessed => assessed.Word).ToArray();
            var moreMenu = OpenMenu("More ways to choose words");
            var pickedWords = moreMenu.GetLogicalDescendants().OfType<TextBox>()
                .Single(input => AutomationProperties.GetName(input) == "Words picked by hand");
            pickedWords.Text = string.Join(Environment.NewLine, pastedWords);
            PageScreenshots.Settle(walkthrough.Window);
            var pickWords = MenuButton(moreMenu, "Pick words");
            Assert.True(pickWords.IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(pickWords)));
            ClickMenu(moreMenu, "Pick words");
            Assert.False(walkthrough.Find<Button>("More ways to choose words").Flyout!.IsOpen);
            walkthrough.WaitUntil(() => !timing.UsePickedWordsCommand.IsRunning &&
                    timing.SelectedWords.SequenceEqual(pastedWords, StringComparer.Ordinal),
                sourceCommandTimeout, "Timing did not load the picked words");
            Assert.Equal(pastedWords, timing.SelectedWords);

            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.Click("Open Text coverage in Texts");
            var comparedWord = walkthrough.Workspace.Assess.Compare.Words.First().Word;
            walkthrough.ShowTextsTab(TextsTab.Matrix);
            // Lists offers the same tick on its own tab, so name the Matrix's list of words.
            var matrixWords = walkthrough.Find<ListBox>("Words in the chosen cells");
            var tick = matrixWords.GetVisualDescendants().OfType<CheckBox>().Single(box =>
                AutomationProperties.GetName(box) == $"Tick {comparedWord}");
            HeadlessClick.Click(walkthrough.Window, tick, $"Tick {comparedWord}");
            Assert.True(tick.IsChecked);
            walkthrough.ShowPage(WorkspacePage.Timing);
            var chosenMenu = OpenMenu("More ways to choose words");
            var chosenWords = MenuButton(chosenMenu, "Chosen in Texts");
            Assert.True(chosenWords.IsEffectivelyEnabled);
            Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(chosenWords)));
            var pickedScope = timing.ScopeLabel;
            ClickMenu(chosenMenu, "Chosen in Texts");
            Assert.False(walkthrough.Find<Button>("More ways to choose words").Flyout!.IsOpen);
            walkthrough.WaitUntil(() => !timing.UseCheckedWordsCommand.IsRunning &&
                    timing.KindTiming is { WordCount: 1 },
                sourceCommandTimeout, "Timing did not load the ticked word from Texts");
            Assert.NotEqual(pickedScope, timing.ScopeLabel);

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
            WalkthroughSteps.EnsureAssessmentForAnalyze(walkthrough, TimeSpan.FromMinutes(3));
            walkthrough.Workspace.Selection.AllWordforms = false;
            foreach (var text in walkthrough.Workspace.Selection.Texts) text.IsChecked = false;

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
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.Matrix);
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

            Assert.False(walkthrough.Workspace.Context.NeedsAssessment);
            var projectLocator = new ProjectLocator(
                Path.GetFullPath(project.FwDataPath), Path.GetFileNameWithoutExtension(project.FwDataPath));
            using var database = MotifDatabase.OpenOwned(
                ProjectDatabaseCatalog.DatabasePathFor(projectLocator), projectLocator,
                MotifSchema.CurrentSchema, new Version(1, 0));
            var invocations = new RetainedInvocationRepository(database).List(
                ProjectWorkspaceKey.Compute(projectLocator));
            Assert.Equal(2, invocations.Count);
            Assert.Contains(invocations, invocation => invocation.InvocationId == result.InvocationId);
            var setupInvocation = invocations.Single(invocation => invocation.InvocationId != result.InvocationId);
            Assert.Equal(["motifanalysed", "motifunanalysed"],
                setupInvocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));
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
