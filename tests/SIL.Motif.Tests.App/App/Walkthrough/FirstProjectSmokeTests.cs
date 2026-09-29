using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FirstProjectSmokeTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AFirstProjectOpensCapturesSetsUpAndShowsItsFirstRun()
    {
        using var project = new TwoTextWalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
        var parser = FakeParser.Copy(project.ManagedRoot);
        var batchStarted = Path.Combine(project.ManagedRoot, "first-batch-started");
        var releaseBatch = Path.Combine(project.ManagedRoot, "release-first-batch");

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            walkthrough.Window.Height = 1100;
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 2, WalkthroughSteps.Remaining(deadline));

            var setup = walkthrough.Workspace.Context.Setup!;
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, true);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new
                    {
                        startedPath = batchStarted,
                        holdUntilPath = releaseBatch,
                        words = new[] { new { word = "motifa", outcome = "complete" } },
                    },
                    ["parse"] = new
                    {
                        traceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"unlistedword\",\"children\":[" +
                            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"SeededRule\",\"children\":[" +
                            "{\"type\":\"Successful\",\"children\":[]}]}]}",
                    },
                },
            });
            try
            {
                walkthrough.Click("Start first run");
                walkthrough.WaitUntil(() => File.Exists(batchStarted), WalkthroughSteps.Remaining(deadline),
                    "the first run did not reach the fake parser");
                Assert.Equal(RunState.Running, walkthrough.Workspace.Assess.State);
                Assert.False(File.Exists(releaseBatch));
            }
            finally
            {
                File.WriteAllText(releaseBatch, string.Empty);
            }
            walkthrough.WaitUntil(() => !setup.IsOpen &&
                walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the released first run did not reach the pages");

            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.WaitUntil(() => walkthrough.Workspace.PageModel<OverviewPageModel>().Overview is not null,
                WalkthroughSteps.Remaining(deadline), "Overview did not load");
            Assert.True(walkthrough.Find<Border>("Project summary").IsEffectivelyVisible);

            walkthrough.ShowPage(WorkspacePage.Warnings);
            walkthrough.WaitUntil(() => walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar.HasChecked,
                WalkthroughSteps.Remaining(deadline), "Warnings did not load the grammar check");
            var checkAgain = walkthrough.Window.GetLogicalDescendants().OfType<Button>().Single(button =>
                Avalonia.Automation.AutomationProperties.GetName(button) == "Check the grammar again" &&
                button.IsEffectivelyVisible);
            HeadlessClick.Click(walkthrough.Window, checkAgain, "Check the grammar again");
            var grammar = walkthrough.Workspace.PageModel<WarningsPageModel>().Grammar;
            walkthrough.WaitUntil(() => !grammar.IsLoading && grammar.ShowFindings,
                WalkthroughSteps.Remaining(deadline), "the grammar findings did not become usable");
            walkthrough.Click("Empty item representation");
            var findingsGrid = walkthrough.Find<DataGrid>("Grammar warnings");
            Assert.True(findingsGrid.IsEffectivelyVisible);
            Assert.Contains(grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>(), row =>
                row.SubjectParts.Any(part => part.FieldWorksLink is not null));
            walkthrough.Window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            walkthrough.Window.UpdateLayout();
            var fieldWorksLinks = walkthrough.Window.GetVisualDescendants().OfType<HyperlinkButton>()
                .Where(link => link.Classes.Contains("warningObjectLink")).ToArray();
            var realizedRows = findingsGrid.GetVisualDescendants().OfType<DataGridRow>().ToArray();
            Assert.True(fieldWorksLinks.Any(link => link.IsEffectivelyVisible),
                $"selected='{grammar.Warnings.SelectedGroup?.Name}', findings={grammar.Warnings.Rows.Count}, " +
                $"grid={findingsGrid.Bounds}, realizedRows={realizedRows.Length}, " +
                $"links={fieldWorksLinks.Length}");

            walkthrough.ShowPage(WorkspacePage.TryAWord);
            walkthrough.Type("Word to try", "unlistedword");
            walkthrough.Click("Try the word");
            var trace = walkthrough.Workspace.PageModel<TryWordPageModel>().Trace;
            walkthrough.WaitUntil(() => trace.HasResult,
                WalkthroughSteps.Remaining(deadline), "Try a Word did not show its trace");
            var derivation = walkthrough.Find<Expander>("Full derivation tree");
            HeadlessClick.Click(walkthrough.Window,
                derivation.GetVisualDescendants().OfType<ToggleButton>().First(), "Full derivation tree");
            Assert.True(derivation.IsExpanded);
            var tree = walkthrough.Find<TreeView>("Filtered full derivation tree");
            walkthrough.WaitUntil(() => tree.GetVisualDescendants().OfType<TreeViewItem>().Any(),
                WalkthroughSteps.Remaining(deadline), "the trace did not show a step to open");
            HeadlessClick.Click(walkthrough.Window,
                tree.GetVisualDescendants().OfType<TreeViewItem>().First(), "a derivation step");
            Assert.NotNull(trace.SelectedStep);

            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.Lists);
            Assert.True(walkthrough.Named<ContentControl>("ListsHost").IsEffectivelyVisible);
            walkthrough.Click("Open the Approved, parsed differently word list");
            Assert.True(walkthrough.Find<ListBox>("Words in the selected list").IsEffectivelyVisible);

            walkthrough.ShowPage(WorkspacePage.Timing);
            Assert.True(walkthrough.Find<Button>("Pick words").IsEffectivelyVisible);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.Click("What changed tab");
            Assert.Equal(TextsTab.WhatChanged, walkthrough.Workspace.PageModel<TextsPageModel>().Tab);
            Assert.True(walkthrough.Named<ContentControl>("DifferenceHost").IsEffectivelyVisible);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
