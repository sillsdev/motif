using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class RestartAndSwitchWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void RestartingAndSwitchingProjectsKeepsOnlyTheSelectedProjectState()
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
                firstWalkthrough.Check(SeededProject.TextTitle);
                Assert.NotNull(firstWalkthrough.Workspace.Baseline.Token);
                firstToken = firstWalkthrough.Workspace.Baseline.Token!;
            }

            using var restartedWalkthrough = new WalkthroughWindow(
                firstProject.ManagedRoot, firstProject.FwDataPath);
            restartedWalkthrough.Show();
            restartedWalkthrough.LoadKnownProjects();
            restartedWalkthrough.SelectKnownProject(firstProject.FwDataPath);
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.HasBaseline &&
                    restartedWalkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline),
                "restarting and choosing the first project did not reload its Baseline and Texts");
            restartedWalkthrough.SkipSetup();
            Assert.Equal(firstToken, restartedWalkthrough.Workspace.Baseline.Token);
            Assert.Equal(
                SeededProject.TextTitle,
                Assert.Single(restartedWalkthrough.Workspace.Selection.Texts).Title);

            Assert.Single(restartedWalkthrough.Workspace.Project.KnownProjects);
            Assert.Equal(firstProject.FwDataPath,
                Assert.Single(restartedWalkthrough.Workspace.Project.KnownProjects).FullFwDataPath);
            restartedWalkthrough.OpenProjectMenu();
            Assert.False(restartedWalkthrough.FindProjectMenuEntry<Button>("Open a recent project").IsEffectivelyEnabled);
            restartedWalkthrough.Click("Project menu");

            using var secondProject = new WalkthroughProject(pristine);
            restartedWalkthrough.ProjectPath = secondProject.FwDataPath;
            restartedWalkthrough.ChooseNewProject();
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                    restartedWalkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                WalkthroughSteps.Remaining(deadline),
                "switching to the second project did not clear the Baseline and its Texts");
            restartedWalkthrough.SkipSetup();
            Assert.Empty(restartedWalkthrough.Workspace.Selection.Texts);
            Assert.Equal(
                "Capture a Baseline to choose Texts.",
                restartedWalkthrough.Workspace.Selection.TextsEmptyMessage);
            Assert.False(restartedWalkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            Assert.False(restartedWalkthrough.Workspace.Context.HasEvidence);
            Assert.Equal("Nothing selected yet.", restartedWalkthrough.Workspace.Selection.SummaryText);

            var simulator = new FieldWorksSimulator(secondProject.FwDataPath);
            using (simulator.Hold())
            {
                ChooseProject(restartedWalkthrough, secondProject.FwDataPath);
                restartedWalkthrough.WaitUntil(
                    () => restartedWalkthrough.Workspace.Baseline.HeldStatusText ==
                            "FieldWorks holds this project open right now." &&
                        restartedWalkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                    WalkthroughSteps.Remaining(deadline),
                    "choosing the held second project did not observe its lock file");
                restartedWalkthrough.SkipSetup();
                restartedWalkthrough.Click("Refresh the project");
                restartedWalkthrough.WaitUntil(
                    () => restartedWalkthrough.Workspace.Baseline.HasBaseline &&
                        restartedWalkthrough.Workspace.Selection.Texts.Count == 1 &&
                        restartedWalkthrough.Workspace.Context.Setup?.IsOpen == true,
                    WalkthroughSteps.Remaining(deadline),
                    "capturing a Baseline for the held second project did not show setup");
                restartedWalkthrough.SkipSetup();
                Assert.Null(restartedWalkthrough.Workspace.Baseline.ShownRefusal);
                Assert.Null(restartedWalkthrough.Workspace.Selection.ShownRefusal);
                Assert.True(File.Exists(secondProject.FwDataPath + ".lock"));
                Assert.Equal(
                    SeededProject.TextTitle,
                    Assert.Single(restartedWalkthrough.Workspace.Selection.Texts).Title);
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
            walkthrough.SelectKnownProject(projectPath);
            return;
        }

        walkthrough.ProjectPath = projectPath;
        walkthrough.ChooseNewProject();
    }

}
