using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class KnownProjectsRefreshWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CapturedProjectAppearsInOpenRecentWithoutRestart()
    {
        using var capturedProject = new WalkthroughProject(pristine);
        using var currentProject = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(capturedProject.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                capturedProject.ManagedRoot, capturedProject.FwDataPath, parserPath: parserPath);
            walkthrough.Show();
            await walkthrough.Workspace.SetProjectAsync(capturedProject.FwDataPath);
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the captured project did not finish its first Refresh");
            walkthrough.SkipSetup();

            await walkthrough.Workspace.SetProjectAsync(currentProject.FwDataPath);
            walkthrough.Click("Project menu");
            var openRecent = walkthrough.FindProjectMenuEntry<Button>("Open a recent project");
            HeadlessClick.Click(walkthrough.Window, openRecent, "Open a recent project");

            var recent = Assert.Single(walkthrough.Workspace.RecentProjects);
            Assert.Equal(capturedProject.FwDataPath, recent.FullFwDataPath);
            var menuItem = Assert.Single(walkthrough.Window.RecentProjectItems);
            Assert.Equal(recent.AutomationName,
                Avalonia.Automation.AutomationProperties.GetName(menuItem));
            Assert.True(menuItem.IsEffectivelyEnabled);
            return;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
