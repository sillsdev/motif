using System.Diagnostics;
using System.Security.Cryptography;
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
            restartedWalkthrough.Click("Browse for a FieldWorks project file");
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.HasBaseline &&
                    restartedWalkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline),
                "restarting and choosing the first project did not reload its Baseline and Texts");
            Assert.Equal(firstToken, restartedWalkthrough.Workspace.Baseline.Token);
            Assert.Equal(
                SeededProject.TextTitle,
                Assert.Single(restartedWalkthrough.Workspace.Selection.Texts).Title);

            var knownProjects = restartedWalkthrough.Find<ComboBox>("Known projects");
            Assert.Equal(1, knownProjects.ItemCount);
            Assert.Equal(firstProject.FwDataPath,
                Assert.Single(restartedWalkthrough.Workspace.Project.KnownProjects).FullFwDataPath);

            using var secondProject = new WalkthroughProject(pristine);
            restartedWalkthrough.ProjectPath = secondProject.FwDataPath;
            restartedWalkthrough.Click("Browse for a FieldWorks project file");
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.CapturedTimeText ==
                        "No Baseline captured yet",
                WalkthroughSteps.Remaining(deadline),
                "switching to the second project did not clear the Baseline");
            Assert.Empty(restartedWalkthrough.Workspace.Selection.Texts);
            Assert.Equal(
                "Capture a Baseline to choose Texts.",
                restartedWalkthrough.Workspace.Selection.TextsEmptyMessage);
            Assert.False(restartedWalkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            Assert.False(restartedWalkthrough.Workspace.HasEverAssessed);
            Assert.Equal("Nothing selected yet.", restartedWalkthrough.Workspace.Selection.SummaryText);

            var lockPath = secondProject.FwDataPath + ".lock";
            var lockBytes = new byte[] { 0x4D, 0x6F, 0x74, 0x69, 0x66, 0x2D, 0x6C, 0x6F, 0x63, 0x6B };
            File.WriteAllBytes(lockPath, lockBytes);
            ChooseProject(restartedWalkthrough, secondProject.FwDataPath);
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.HeldStatusText ==
                    "FieldWorks holds this project open right now.",
                WalkthroughSteps.Remaining(deadline),
                "choosing the held second project did not observe its lock file");
            restartedWalkthrough.Click("Refresh the Baseline");
            restartedWalkthrough.WaitUntil(
                () => restartedWalkthrough.Workspace.Baseline.HasBaseline &&
                    restartedWalkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline),
                "capturing a Baseline for the held second project did not complete");
            Assert.Null(restartedWalkthrough.Workspace.Baseline.RefusalMessage);
            Assert.Null(restartedWalkthrough.Workspace.Selection.RefusalMessage);
            Assert.True(File.Exists(lockPath));
            Assert.Equal(lockBytes, File.ReadAllBytes(lockPath));
            Assert.Equal(
                SeededProject.TextTitle,
                Assert.Single(restartedWalkthrough.Workspace.Selection.Texts).Title);

            Assert.Equal(firstProject.SourceSha256, Sha256(firstProject.FwDataPath));
            Assert.Equal(secondProject.SourceSha256, Sha256(secondProject.FwDataPath));
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void ChooseProject(WalkthroughWindow walkthrough, string projectPath)
    {
        if (walkthrough.Workspace.Project.KnownProjects.Any(known =>
                string.Equals(known.FullFwDataPath, projectPath, StringComparison.OrdinalIgnoreCase)))
        {
            walkthrough.SelectKnownProject(projectPath);
            return;
        }

        walkthrough.ProjectPath = projectPath;
        walkthrough.Click("Browse for a FieldWorks project file");
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
