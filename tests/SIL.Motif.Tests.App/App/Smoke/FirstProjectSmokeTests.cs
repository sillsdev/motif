using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Smoke;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FirstProjectSmokeTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AFirstProjectOpensCapturesSetsUpAndShowsItsFirstRun()
    {
        using var project = new TwoTextWalkthroughProject(pristine);
        var deadline = WalkthroughSteps.DeadlineIn(120);
        var parser = FakeParser.Copy(project.ManagedRoot);
        var batchStarted = Path.Combine(project.ManagedRoot, "first-batch-started");
        var releaseBatch = Path.Combine(project.ManagedRoot, "release-first-batch");
        var measurementStarted = Path.Combine(project.ManagedRoot, "review-measurement-started");
        var releaseMeasurement = Path.Combine(project.ManagedRoot, "release-review-measurement");
        var firstReading = new
        {
            morphs = new[]
            {
                new
                {
                    form = pristine.Seed.FirstLexemeFormId.ToString("D"),
                    msa = project.FirstMsaId.ToString("D"),
                    inflType = (string?)null,
                    guessedString = (string?)null,
                },
            },
        };

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parser);
            walkthrough.Show();
            InteractiveControlSweep.AssertScene(walkthrough, "no project selected",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Collection);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 2, StepTimeout(deadline));
            InteractiveControlSweep.AssertScene(walkthrough, "first project setup",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Collection);

            var setup = walkthrough.Workspace.Context.Setup!;
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextTexts);
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, true);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextLimits);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, SIL.Motif.App.MotifAppStrings.SetupNextFirstRun);
            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new
                    {
                        startedPath = batchStarted,
                        holdUntilPath = releaseBatch,
                        words = new object[]
                        {
                            new
                            {
                                word = SeededProject.AnalysedWordForm,
                                outcome = "complete",
                                signature = "authored-reading",
                                analyses = new[] { firstReading },
                            },
                            new
                            {
                                word = SeededProject.UnanalysedWordForm,
                                outcome = "no-analysis",
                                signature = "-",
                            },
                        },
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
                walkthrough.Click(SIL.Motif.App.MotifAppStrings.StartFirstRun);
                walkthrough.WaitUntil(() => File.Exists(batchStarted), StepTimeout(deadline),
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
                StepTimeout(deadline), "the released first run did not reach the pages");

            walkthrough.ShowPage(WorkspacePage.Progress);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Context.Health.Overview is not null,
                StepTimeout(deadline), "Overview did not load");
            InteractiveControlSweep.AssertScene(walkthrough, "completed Overview",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Collection);
            Assert.True(ProjectCardAccess.Chip(walkthrough.Window).IsEffectivelyVisible);
            walkthrough.ShowPage(WorkspacePage.Words);
            Assert.Equal(WorkspacePage.Words, walkthrough.Workspace.CurrentPage);

            var changes = walkthrough.Workspace.Context.Changes;
            await changes.RemoveAnalysisAsync(
                CanonicalId.FromGuid(project.FirstText.AnalysedWordformId).Value,
                SeededProject.AnalysedWordForm,
                CanonicalId.FromGuid(project.FirstText.ApprovedAnalysisId).Value);
            await changes.PutAsync(new ChangeIntent(CanonicalId.Mint().Value,
                ChangeKinds.IncorrectSpelling,
                CanonicalId.FromGuid(project.FirstText.UnanalysedWordformId).Value,
                SeededProject.UnanalysedWordForm,
                OriginPage: WorkspacePage.Texts.ToString()));
            Assert.True(changes.Items.Count == 2,
                changes.LastRefusal?.Message ?? $"Expected two changes, found {changes.Items.Count}.");

            walkthrough.ShowPage(WorkspacePage.Proposals);
            var removal = changes.Items.Single(change => change.Kind == ChangeKinds.RemoveAnalysis);
            Assert.Equal("Approved", removal.StagedTransition.Now);
            Assert.Equal("Removed", removal.StagedTransition.AfterApply);
            Assert.Equal(OpinionMarkKind.Approved, removal.NowOpinionMark);
            Assert.Equal(OpinionMarkKind.None, removal.AfterOpinionMark);
            var spelling = changes.Items.Single(change => change.Kind == ChangeKinds.IncorrectSpelling);
            Assert.Equal("Current spelling", spelling.StagedTransition.Now);
            Assert.Equal("Incorrect", spelling.StagedTransition.AfterApply);
            await changes.RemoveCommand.ExecuteAsync(removal);
            Assert.Single(changes.Items);

            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new
                    {
                        startedPath = measurementStarted,
                        holdUntilPath = releaseMeasurement,
                        words = new[] { new { word = SeededProject.UnanalysedWordForm, outcome = "complete" } },
                    },
                    ["parse"] = new
                    {
                        traceJson = "{\"type\":\"WordAnalysis\",\"inputShape\":\"unlistedword\",\"children\":[" +
                            "{\"type\":\"MorphologicalRuleAnalysis\",\"source\":\"SeededRule\",\"children\":[" +
                            "{\"type\":\"Successful\",\"children\":[]}]}]}",
                    },
                },
            });
            var measurement = walkthrough.Workspace.Context.PendingMeasurement;
            var checking = measurement.MeasureCommand.ExecuteAsync(null);
            try
            {
                walkthrough.WaitUntil(() => File.Exists(measurementStarted), StepTimeout(deadline),
                    "the measurement did not reach the fake parser");
                Assert.True(measurement.IsMeasuring);
                Assert.False(File.Exists(releaseMeasurement));
            }
            finally { File.WriteAllText(releaseMeasurement, string.Empty); }
            await checking;
            var apply = walkthrough.Workspace.Context.ProposalApply;
            Assert.True(apply.CanApply(null, null, null, null));
            await apply.ApplyCurrentAsync(null);
            Assert.NotNull(apply.Receipt);
            Assert.Empty(changes.Items);
            walkthrough.WaitUntil(() => !apply.IsRecordingBaseline && !walkthrough.Workspace.RefreshCommand.IsRunning &&
                !walkthrough.Workspace.Context.Evidence.HasAssessment,
                StepTimeout(deadline), "the Refresh after Apply did not finish");
            walkthrough.SetFakeParserBehavior(new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new
                    {
                        words = new object[]
                        {
                            new
                            {
                                word = SeededProject.AnalysedWordForm,
                                outcome = "complete",
                                signature = "authored-reading",
                                analyses = new[] { firstReading },
                            },
                            new
                            {
                                word = SeededProject.UnanalysedWordForm,
                                outcome = "no-analysis",
                                signature = "-",
                            },
                        },
                    },
                },
            });
            walkthrough.ShowPage(WorkspacePage.Texts);
            SetupWalkthroughActions.ClickParseAllWordsFromTexts(walkthrough);
            walkthrough.WaitUntil(() => walkthrough.Workspace.Context.Evidence.HasAssessment &&
                walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                StepTimeout(deadline), "parsing again after Apply did not publish an Assessment");
            walkthrough.Workspace.Context.Diagnostics = true;
            walkthrough.ShowPage(WorkspacePage.Diagnostics);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Warnings",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Collection);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);
            await AnalyzeTextsLayoutTests.SettleReaderAsync(walkthrough.Workspace, walkthrough.Window);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Analyze texts",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Occurrence,
                InteractiveControlFamily.Collection);
            var textsPage = walkthrough.Workspace.PageModel<TextsPageModel>();
            walkthrough.ShowPage(WorkspacePage.Words);
            walkthrough.WaitUntil(() => walkthrough.Window.GetVisualDescendants().OfType<SIL.Motif.App.Views.TextWordsPanel>()
                .Any(panel => panel.IsEffectivelyVisible), WalkthroughSteps.Remaining(deadline), "the Word list did not open");
            InteractiveControlSweep.AssertScene(walkthrough, "completed Word list with opinion counts",
                InteractiveControlFamily.Action, InteractiveControlFamily.Link, InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry, InteractiveControlFamily.Check, InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List, InteractiveControlFamily.SelectableText, InteractiveControlFamily.Mark,
                InteractiveControlFamily.Morpheme, InteractiveControlFamily.FocusableSurface, InteractiveControlFamily.Collection);
            walkthrough.ShowPage(WorkspacePage.Texts);
            await AnalyzeTextsLayoutTests.SettleReaderAsync(walkthrough.Workspace, walkthrough.Window);
            var inText = walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText;
            var panel = AnalyzeTextsLayoutTests.Panel(walkthrough.Window);
            var occurrence = AnalyzeTextsLayoutTests.Strips(panel)
                .Select(strip => Assert.IsType<ResultsTokenViewModel>(SIL.Motif.App.Views.ResultsInTextPanel.TokenOf(strip)))
                .FirstOrDefault(token => token.HasReadings);
            Assert.NotNull(occurrence);
            await inText.OpenTokenCardAsync(occurrence);
            PageScreenshots.Settle(walkthrough.Window);
            InteractiveControlSweep.AssertScene(walkthrough, "word card with a reading",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Occurrence,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.Morpheme);
            return;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static TimeSpan StepTimeout(long deadline)
    {
        var remaining = WalkthroughSteps.Remaining(deadline);
        var parserWorkLimit = TimeSpan.FromSeconds(30);
        return remaining < parserWorkLimit ? remaining : parserWorkLimit;
    }
}
