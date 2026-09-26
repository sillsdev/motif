using System.Diagnostics;
using Avalonia.LogicalTree;
using SIL.LCModel;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ApplyReadBackWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ApplyingFromReviewShowsTheResultAndStalesTheOldNumbersUntilRefresh()
    {
        using var project = new WalkthroughProject(pristine);
        var change = PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "apply-read-back-word");
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: FakeParser.ExecutablePath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true &&
                    walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the prepared project did not open with its pending change");
            walkthrough.SkipSetup();

            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
            Assert.True(walkthrough.Workspace.Context.Evidence.HasAssessment);

            walkthrough.ShowPage(WorkspacePage.Review);
            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            walkthrough.Click("Check what applying does to the numbers");
            walkthrough.WaitUntil(
                () => !review.IsMeasuring && review.ApplyCommand.CanExecute(null),
                WalkthroughSteps.Remaining(deadline), "checking the pending change did not enable Apply");
            Assert.True(walkthrough.Find<Avalonia.Controls.Button>("Apply to FieldWorks project").IsEffectivelyEnabled);

            walkthrough.Click("Apply to FieldWorks project");
            walkthrough.WaitUntil(
                () => review.HasReceipt && review.Changes.Items.Count == 0,
                WalkthroughSteps.Remaining(deadline), "Apply did not show its Receipt and clear the pending change");

            Assert.True(walkthrough.Workspace.Context.Evidence.AppliedSinceRefresh);
            Assert.True(walkthrough.Workspace.Context.Evidence.IsStale);
            Assert.Contains("Changes applied to", review.ReceiptText, StringComparison.Ordinal);
            var receipt = walkthrough.Window.GetLogicalDescendants().OfType<CopyableTextBlock>()
                .Single(text => text.Text == review.ReceiptText);
            Assert.True(receipt.IsVisible);

            using (var cache = new FwDataProjectLoader().LoadCache(project.FwDataPath))
            {
                var wordform = cache.ServiceLocator.GetInstance<SIL.LCModel.IWfiWordformRepository>()
                    .GetObject(change.WordformId);
                Assert.Equal(2, wordform.SpellingStatus);
            }

            Assert.Equal("Numbers need refresh", walkthrough.Workspace.FreshnessLabel);
            Assert.Contains("stale until you refresh", walkthrough.Workspace.FreshnessDetail,
                StringComparison.Ordinal);

            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Workspace.Assess.State == RunState.Completed &&
                    !walkthrough.Workspace.Context.Evidence.IsStale,
                WalkthroughSteps.Remaining(deadline), "Refresh did not replace the stale numbers");
            Assert.Equal("Refreshed", walkthrough.Workspace.FreshnessLabel);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
