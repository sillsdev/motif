using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.LCModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ApplyReadBackWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ApplyingFromReviewShowsTheReceiptClearsTheBadgeAndStalesTheNumbersUntilRefresh()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var change = PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "apply-read-back-word");
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true &&
                    walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the prepared project did not open with its pending change");

            SetupWalkthroughActions.FinishFirstRun(
                walkthrough, SeededProject.TextTitle, StepCap.DefaultSteps.ToString(),
                WalkthroughSteps.Remaining(deadline));
            walkthrough.SetFakeParserBehavior(new
            {
                words = new[]
                {
                    new { word = SeededProject.AnalysedWordForm, outcome = "complete", signature = "before-refresh" },
                },
            });
            var batchesBeforeManualAssessment = FakeParser.Invocations(parserPath).Count(command => command == "batch");
            walkthrough.TypePastedWords(SeededProject.AnalysedWordForm);
            walkthrough.Click("Run the Assessment");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted &&
                    FakeParser.Invocations(parserPath).Count(command => command == "batch") > batchesBeforeManualAssessment,
                WalkthroughSteps.Remaining(deadline), "the initial Assessment did not publish its fake measurement");
            Assert.True(walkthrough.Workspace.Context.Evidence.HasAssessment);
            var beforeRefreshResult = Assert.IsType<AssessCommandResponse>(walkthrough.Workspace.Assess.Result);
            var beforeRefreshWord = Assert.Single(beforeRefreshResult.Words,
                word => word.Word == SeededProject.AnalysedWordForm);
            Assert.Equal("before-refresh", beforeRefreshWord.RawSignature);
            var beforeRefreshToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                walkthrough.Workspace.Baseline.Token);
            var beforeRefreshSourceLastWrite = Assert.IsType<DateTimeOffset>(
                walkthrough.Workspace.Baseline.SourceLastWriteUtc);

            walkthrough.ShowPage(WorkspacePage.Review);
            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            var reviewEntry = walkthrough.Window.GetLogicalDescendants().OfType<ListBoxItem>()
                .Single(item => Avalonia.Automation.AutomationProperties.GetName(item) == "Review changes page");
            var reviewBadge = reviewEntry.GetLogicalDescendants().OfType<Border>()
                .Single(border => border.Classes.Contains("pageBadge"));
            Assert.True(reviewBadge.IsEffectivelyVisible);
            Assert.Equal("1", reviewBadge.GetLogicalDescendants().OfType<CopyableTextBlock>().Single().Text);

            walkthrough.Click("Check what applying does to the numbers");
            walkthrough.WaitUntil(
                () => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                WalkthroughSteps.Remaining(deadline), "checking the pending change did not enable Apply");
            Assert.True(walkthrough.Find<Button>("Apply to FieldWorks project").IsEffectivelyEnabled);

            walkthrough.Click("Apply to FieldWorks project");
            walkthrough.WaitUntil(
                () => review.HasReceipt && review.Changes.Items.Count == 0,
                WalkthroughSteps.Remaining(deadline), "Apply did not show its Receipt and clear the pending change");

            Assert.True(walkthrough.Workspace.Context.Evidence.AppliedSinceRefresh);
            Assert.True(walkthrough.Workspace.Context.Evidence.IsStale);
            Assert.False(reviewBadge.IsEffectivelyVisible);
            Assert.Contains("Changes applied to", review.ReceiptText, StringComparison.Ordinal);
            var receiptTitle = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == "Applied to the FieldWorks project");
            Assert.True(receiptTitle.IsEffectivelyVisible);
            var receipt = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == review.ReceiptText);
            Assert.True(receipt.IsEffectivelyVisible);

            using (var cache = new FwDataProjectLoader().LoadScratchCache(project.FwDataPath))
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                    .GetObject(change.WordformId);
                Assert.Equal(2, wordform.SpellingStatus);
            }

            var freshnessLabel = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Classes.Contains("freshLabel"));
            var freshnessDetail = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Classes.Contains("freshDetail"));
            Assert.True(freshnessLabel.IsEffectivelyVisible);
            Assert.Equal("Numbers need refresh", freshnessLabel.Text);
            Assert.True(freshnessDetail.IsEffectivelyVisible);
            Assert.Contains("stale until you refresh", freshnessDetail.Text,
                StringComparison.Ordinal);

            var batchInvocationsBeforeRefresh = FakeParser.Invocations(parserPath)
                .Count(command => command == "batch");
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Workspace.Freshness == ProjectFreshness.Refreshed &&
                    walkthrough.Workspace.Baseline.Token != beforeRefreshToken,
                WalkthroughSteps.Remaining(deadline), "Refresh did not capture a new Baseline");
            Assert.Null(walkthrough.Workspace.Context.Changes.ShownRefusal);
            Assert.Equal(batchInvocationsBeforeRefresh,
                FakeParser.Invocations(parserPath).Count(command => command == "batch"));
            Assert.NotEqual(beforeRefreshToken, walkthrough.Workspace.Baseline.Token);
            var refreshedSourceLastWrite = Assert.IsType<DateTimeOffset>(
                walkthrough.Workspace.Baseline.SourceLastWriteUtc);
            Assert.NotEqual(beforeRefreshSourceLastWrite, refreshedSourceLastWrite);
            Assert.Equal(File.GetLastWriteTimeUtc(project.FwDataPath), refreshedSourceLastWrite.UtcDateTime);
            Assert.Null(walkthrough.Workspace.Context.Evidence.Assessment);
            Assert.True(walkthrough.Workspace.Context.NeedsAssessment);
            Assert.Equal("Refreshed. Parse all words to update the numbers.",
                walkthrough.Workspace.FreshnessDetail);
            Assert.True(freshnessLabel.IsEffectivelyVisible);
            Assert.Equal("Refreshed", freshnessLabel.Text);
            Assert.True(freshnessDetail.IsEffectivelyVisible);
            Assert.Equal(walkthrough.Workspace.FreshnessDetail, freshnessDetail.Text);

            var startedPath = Path.Combine(project.ManagedRoot, "refreshed-parser-started");
            var releasePath = Path.Combine(project.ManagedRoot, "release-refreshed-parser");
            walkthrough.SetFakeParserBehavior(new
            {
                startedPath,
                holdUntilPath = releasePath,
                words = new[]
                {
                    new
                    {
                        word = SeededProject.AnalysedWordForm,
                        outcome = "complete",
                        signature = "after-refresh",
                    },
                },
            });
            walkthrough.ShowPage(WorkspacePage.Texts);
            SetupWalkthroughActions.ClickParseAllWordsFromTexts(walkthrough);
            try
            {
                walkthrough.WaitUntil(
                    () => File.Exists(startedPath) || walkthrough.Workspace.Assess.State is
                        RunState.Completed or RunState.Refused or RunState.Cancelled,
                    WalkthroughSteps.Remaining(deadline), "Parse all words did not reach the recording fake");
                Assert.True(File.Exists(startedPath), "Parse all words completed without starting the fake parser.");
                var invocationsWhileHeld = FakeParser.Invocations(parserPath);
                Assert.Equal(batchInvocationsBeforeRefresh + 1,
                    invocationsWhileHeld.Count(command => command == "batch"));
                Assert.Equal("batch", invocationsWhileHeld[^1]);
            }
            finally
            {
                File.WriteAllText(releasePath, string.Empty);
            }

            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the explicit Parse all words action did not finish");
            var afterRefreshResult = Assert.IsType<AssessCommandResponse>(walkthrough.Workspace.Assess.Result);
            var refreshedWord = Assert.Single(afterRefreshResult.Words,
                word => word.Word == SeededProject.AnalysedWordForm);
            Assert.Equal("after-refresh", refreshedWord.RawSignature);
            Assert.NotEqual(beforeRefreshWord.RawSignature, refreshedWord.RawSignature);
            Assert.Equal(
                batchInvocationsBeforeRefresh + 1,
                FakeParser.Invocations(parserPath).Count(command => command == "batch"));
            Assert.Equal("after-refresh", Assert.Single(
                walkthrough.Workspace.Context.Evidence.Words,
                word => word.Word == SeededProject.AnalysedWordForm).RawSignature);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
