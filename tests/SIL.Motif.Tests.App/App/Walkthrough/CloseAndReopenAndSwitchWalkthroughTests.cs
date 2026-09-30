using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CloseAndReopenAndSwitchWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ClosingAndReopeningAndSwitchingProjectsKeepsOnlyTheSelectedProjectState()
    {
        using var firstProject = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            BaselineToken firstToken;
            using (var firstWalkthrough = new WalkthroughWindow(
                       firstProject.ManagedRoot, firstProject.FwDataPath))
            {
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(firstWalkthrough, deadline);
                Assert.NotNull(firstWalkthrough.Workspace.Baseline.Token);
                firstToken = firstWalkthrough.Workspace.Baseline.Token!;
            }

            using var reopenedWalkthrough = new WalkthroughWindow(
                firstProject.ManagedRoot, firstProject.FwDataPath);
            reopenedWalkthrough.Show();
            reopenedWalkthrough.OpenRecentProjectByClick(firstProject.FwDataPath);
            reopenedWalkthrough.WaitUntil(
                () => reopenedWalkthrough.Workspace.Baseline.HasBaseline &&
                    reopenedWalkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline),
                "closing, reopening and choosing the first project did not reload its Baseline and Texts");
            reopenedWalkthrough.SkipSetup();
            Assert.Equal(firstToken, reopenedWalkthrough.Workspace.Baseline.Token);
            Assert.Equal(
                SeededProject.TextTitle,
                Assert.Single(reopenedWalkthrough.Workspace.Selection.Texts).Title);

            Assert.Single(reopenedWalkthrough.Workspace.Project.KnownProjects);
            Assert.Equal(firstProject.FwDataPath,
                Assert.Single(reopenedWalkthrough.Workspace.Project.KnownProjects).FullFwDataPath);
            reopenedWalkthrough.OpenProjectMenu();
            Assert.False(reopenedWalkthrough.FindProjectMenuEntry<Button>("Open a recent project").IsEffectivelyEnabled);
            reopenedWalkthrough.CloseProjectMenu();

            using var secondProject = new WalkthroughProject(pristine);
            reopenedWalkthrough.ProjectPath = secondProject.FwDataPath;
            reopenedWalkthrough.ChooseNewProject();
            reopenedWalkthrough.WaitUntil(
                () => reopenedWalkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                    reopenedWalkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                WalkthroughSteps.Remaining(deadline),
                "switching to the second project did not clear the Baseline and its Texts");
            reopenedWalkthrough.SkipSetup();
            Assert.Empty(reopenedWalkthrough.Workspace.Selection.Texts);
            Assert.Equal(
                "Capture a Baseline to choose Texts.",
                reopenedWalkthrough.Workspace.Selection.TextsEmptyMessage);
            Assert.False(reopenedWalkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);
            Assert.False(reopenedWalkthrough.Workspace.Context.HasEvidence);
            Assert.Equal("Nothing selected yet.", reopenedWalkthrough.Workspace.Selection.SummaryText);

            var simulator = new FieldWorksSimulator(secondProject.FwDataPath);
            using (simulator.Hold())
            {
                ChooseProject(reopenedWalkthrough, secondProject.FwDataPath);
                reopenedWalkthrough.WaitUntil(
                    () => reopenedWalkthrough.Workspace.Baseline.HeldStatusText ==
                            "FieldWorks holds this project open right now." &&
                        reopenedWalkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                    WalkthroughSteps.Remaining(deadline),
                    "choosing the held second project did not observe its lock file");
                reopenedWalkthrough.SkipSetup();
                reopenedWalkthrough.Click("Refresh the project");
                reopenedWalkthrough.WaitUntil(
                    () => reopenedWalkthrough.Workspace.Baseline.HasBaseline &&
                        reopenedWalkthrough.Workspace.Selection.Texts.Count == 1 &&
                        reopenedWalkthrough.Workspace.Context.Setup?.IsOpen == true,
                    WalkthroughSteps.Remaining(deadline),
                    "capturing a Baseline for the held second project did not show setup");
                reopenedWalkthrough.SkipSetup();
                Assert.Null(reopenedWalkthrough.Workspace.Baseline.ShownRefusal);
                Assert.Null(reopenedWalkthrough.Workspace.Selection.ShownRefusal);
                Assert.True(File.Exists(secondProject.FwDataPath + ".lock"));
                Assert.Equal(
                    SeededProject.TextTitle,
                    Assert.Single(reopenedWalkthrough.Workspace.Selection.Texts).Title);
            }

            Assert.Equal(firstProject.SourceSha256, WalkthroughStoreAssertions.Sha256(firstProject.FwDataPath));
            Assert.Equal(secondProject.SourceSha256, WalkthroughStoreAssertions.Sha256(secondProject.FwDataPath));
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void ChooseProject(WalkthroughWindow walkthrough, string projectPath)
    {
        if (walkthrough.Workspace.RecentProjects.Any(known =>
                string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase)))
        {
            walkthrough.OpenRecentProjectByClick(projectPath);
            return;
        }

        walkthrough.ProjectPath = projectPath;
        walkthrough.ChooseNewProject();
    }

}
