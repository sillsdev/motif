using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FieldWorksSimulatorWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FieldWorksHoldingTheProjectShowsHeldAndBlocksApply()
    {
        using var project = new WalkthroughProject(pristine);
        PendingChangeFixture.AddIncorrectSpelling(
            project.FwDataPath, project.ManagedRoot, "held-change-word");
        using var held = new FieldWorksSimulator(project.FwDataPath).Hold();
        var deadline = Stopwatch.GetTimestamp() + 90 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            walkthrough.Show();
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.FieldWorksHeldProject &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the window did not show the held project and its setup");
            walkthrough.SkipSetup();
            walkthrough.ShowPage(WorkspacePage.Review);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Changes.Items.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the pending change did not appear in Review");

            var review = walkthrough.Workspace.PageModel<ReviewPageModel>();
            Assert.False(review.CanApply);
            Assert.False(review.ApplyCommand.CanExecute(null));
            Assert.Equal("FieldWorks has this project open. Close it before applying changes.",
                review.ApplyBlockReason);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
