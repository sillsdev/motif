using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
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
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
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
                InteractiveControlFamily.ContentSurface,
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
                InteractiveControlFamily.ContentSurface,
                InteractiveControlFamily.Collection);

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
                walkthrough.Click("Start first run");
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

            walkthrough.ShowPage(WorkspacePage.Overview);
            walkthrough.WaitUntil(() => walkthrough.Workspace.PageModel<OverviewPageModel>().Overview is not null,
                StepTimeout(deadline), "Overview did not load");
            InteractiveControlSweep.AssertSceneWithReportedGaps(walkthrough, "completed Overview",
                nameof(AFirstProjectOpensCapturesSetsUpAndShowsItsFirstRun),
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.ContentSurface,
                InteractiveControlFamily.Summary,
                InteractiveControlFamily.StaticText,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.FocusableSurface);
            Assert.True(walkthrough.Find<Border>("Project summary").IsEffectivelyVisible);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.Matrix);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Compare Matrix",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.MatrixCell,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Morpheme,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.Container);
            Assert.Equal(WorkspacePage.Texts, walkthrough.Workspace.CurrentPage);

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

            walkthrough.ShowPage(WorkspacePage.Review);
            InteractiveControlSweep.AssertScene(walkthrough, "pending changes in Review",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.Morpheme,
                InteractiveControlFamily.FocusableSurface);
            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.Equal(["Removed", "Spelling → Incorrect"],
                review.ReviewGroups.Select(group => group.Title));
            var removal = review.ReviewGroups.SelectMany(group => group.Items)
                .Single(change => change.Kind == ChangeKinds.RemoveAnalysis);
            Assert.Equal("Approved", removal.StagedTransition.Now);
            Assert.Equal("Removed", removal.StagedTransition.AfterApply);
            Assert.Equal(OpinionMarkKind.Approved, removal.NowOpinionMark);
            Assert.Equal(OpinionMarkKind.None, removal.AfterOpinionMark);
            var spelling = review.ReviewGroups.SelectMany(group => group.Items)
                .Single(change => change.Kind == ChangeKinds.IncorrectSpelling);
            Assert.Equal("Current spelling", spelling.StagedTransition.Now);
            Assert.Equal("Incorrect", spelling.StagedTransition.AfterApply);

            var openPopups = walkthrough.Window.GetLogicalDescendants().OfType<Popup>()
                .Where(popup => popup.IsOpen)
                .Select(popup => $"target={popup.PlacementTarget?.Name}; child={popup.Child?.GetType().Name}")
                .ToArray();
            Assert.True(openPopups.Length == 0, string.Join("; ", openPopups));
            Assert.False(walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText.SelectedToken?.IsCardOpen ?? false,
                "A word card remained open after leaving Analyze texts.");
            walkthrough.Click($"Undo: {SeededProject.AnalysedWordForm}");
            walkthrough.WaitUntil(() => changes.Items.Count == 1,
                StepTimeout(deadline), "Undo did not remove the selected pending analysis removal");
            Assert.Equal("Spelling → Incorrect", Assert.Single(review.ReviewGroups).Title);

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
            try
            {
                walkthrough.Click("Check what applying does to the numbers");
                walkthrough.WaitUntil(() => File.Exists(measurementStarted), StepTimeout(deadline),
                    "the Review measurement did not reach the fake parser");
                Assert.True(review.IsMeasuring);
                Assert.False(File.Exists(releaseMeasurement));
            }
            finally
            {
                File.WriteAllText(releaseMeasurement, string.Empty);
            }

            walkthrough.WaitUntil(() => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                StepTimeout(deadline), "releasing the parser did not enable Apply");
            var remainingBeforeApply = WalkthroughSteps.Remaining(deadline);
            var applyClock = Stopwatch.StartNew();
            walkthrough.Click("Apply to FieldWorks project");
            var remainingAfterApplyClick = WalkthroughSteps.Remaining(deadline);
            var applyTimeout = StepTimeout(deadline);
            try
            {
                walkthrough.WaitUntil(() => review.HasReceipt && changes.Items.Count == 0,
                    applyTimeout, "Apply did not finish from the Review page");
            }
            catch (Xunit.Sdk.XunitException exception)
            {
                throw new Xunit.Sdk.XunitException(
                    $"{exception.Message}; remaining before Apply='{remainingBeforeApply}', " +
                    $"remaining after click='{remainingAfterApplyClick}', allocated wait='{applyTimeout}', " +
                    $"Apply phase elapsed='{applyClock.Elapsed}', IsApplying='{review.IsApplying}', " +
                    $"HasReceipt='{review.HasReceipt}', CanApply='{review.CanApply}', " +
                    $"ApplyBlockReason='{review.ApplyBlockReason}', " +
                    $"Apply refusal='{review.ApplyRefusal?.Code}: {review.ApplyRefusal?.Sentence}', " +
                    $"Apply task='{review.ApplyCommand.ExecutionTask?.Status}', " +
                    $"Apply fault='{review.ApplyCommand.ExecutionTask?.Exception}', " +
                    $"pending count='{changes.Items.Count}', revision='{changes.Snapshot.Revision}', " +
                    $"reload refusal='{changes.LastRefusal?.Code}: {changes.LastRefusal?.Message}'");
            }
            InteractiveControlSweep.AssertScene(walkthrough, "Review receipt",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Collection);
            walkthrough.ShowPage(WorkspacePage.Timing);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Timing",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Grid,
                InteractiveControlFamily.Morpheme,
                InteractiveControlFamily.Summary,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.ContentSurface,
                InteractiveControlFamily.Collection,
                InteractiveControlFamily.Container);
            walkthrough.ShowPage(WorkspacePage.Warnings);
            InteractiveControlSweep.AssertScene(walkthrough, "completed Warnings",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Collection);
            walkthrough.ShowPage(WorkspacePage.Texts);
            walkthrough.ShowTextsTab(TextsTab.AnalyzeTexts);
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
            var inText = walkthrough.Workspace.PageModel<TextsPageModel>().ResultsInText;
            var panel = AnalyzeTextsLayoutTests.Panel(walkthrough.Window);
            var occurrence = AnalyzeTextsLayoutTests.Strips(panel)
                .Select(strip => Assert.IsType<ResultsTokenViewModel>(strip.Tag))
                .FirstOrDefault(token => token.HasReadings);
            Assert.NotNull(occurrence);
            await inText.OpenTokenCardAsync(occurrence);
            AnalyzeTextsLayoutTests.Settle(walkthrough.Window);
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
        var stepLimit = TimeSpan.FromSeconds(30);
        return remaining < stepLimit ? remaining : stepLimit;
    }
}
