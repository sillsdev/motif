using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class KnownProjectsRefreshWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void NewProjectsAppearAndMissingProjectsLeaveOpenRecent()
    {
        using var firstProject = new WalkthroughProject(pristine);
        using var secondProject = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(firstProject.ManagedRoot, firstProject.FwDataPath);
            walkthrough.Show();

            SelectNewProject(walkthrough, firstProject.FwDataPath);
            CaptureBaseline(walkthrough, deadline);
            walkthrough.SkipSetup();

            SelectNewProject(walkthrough, secondProject.FwDataPath);
            CaptureBaseline(walkthrough, deadline);
            walkthrough.SkipSetup();

            walkthrough.WaitUntil(
                () => walkthrough.Workspace.RecentProjects.Any(project =>
                    string.Equals(project.FullFwDataPath, firstProject.FwDataPath,
                        StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromSeconds(10),
                "the first project did not appear under Open recent after its capture");

            walkthrough.SelectKnownProject(firstProject.FwDataPath);

            walkthrough.WaitUntil(
                () => string.Equals(walkthrough.Workspace.Context.ProjectPath, firstProject.FwDataPath,
                        StringComparison.OrdinalIgnoreCase) &&
                    walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1,
                TimeSpan.FromSeconds(60), "the recent project did not open in the window");

            Assert.Contains(walkthrough.Workspace.RecentProjects, project =>
                string.Equals(project.FullFwDataPath, secondProject.FwDataPath,
                    StringComparison.OrdinalIgnoreCase));
            File.Delete(secondProject.FwDataPath);

            walkthrough.Click("Project menu");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.RecentProjects.All(project =>
                    !string.Equals(project.FullFwDataPath, secondProject.FwDataPath,
                        StringComparison.OrdinalIgnoreCase)),
                TimeSpan.FromSeconds(10), "the missing project stayed under Open recent");
            Assert.False(walkthrough.FindProjectMenuEntry<Button>("Open a recent project").IsEffectivelyEnabled);

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void SelectNewProject(WalkthroughWindow walkthrough, string projectPath)
    {
        walkthrough.ProjectPath = projectPath;
        walkthrough.ChooseNewProject();
        Assert.Null(walkthrough.Workspace.OpenRefusal?.Sentence);
        Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal?.Sentence);
    }

    private static void CaptureBaseline(WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Click("Refresh the project");
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.Texts.Count == 1 &&
                walkthrough.Workspace.Context.Setup?.IsOpen == true,
            WalkthroughSteps.Remaining(deadline), "Refresh did not capture and publish the project Baseline");
        walkthrough.WaitUntil(
            () => !walkthrough.Workspace.RefreshCommand.IsRunning,
            WalkthroughSteps.Remaining(deadline), "Refresh did not finish");
    }

}
