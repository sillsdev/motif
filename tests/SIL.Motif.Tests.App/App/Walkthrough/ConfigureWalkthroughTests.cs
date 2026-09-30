using System.Diagnostics;
using Avalonia.Input;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>Checks that Configure opens once from keyboard input and a double-click.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ConfigureWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ConfigureOpensOnceFromKeyboardAndDoubleClick()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var setup = walkthrough.Workspace.Context.Setup!;
            Assert.False(setup.IsOpen);
            Assert.False(walkthrough.SetupDialogIsShown);
            var page = walkthrough.Workspace.CurrentPage;
            var opened = 0;
            setup.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupViewModel.IsOpen) && setup.IsOpen) opened++;
            };

            foreach (var (key, physicalKey) in new[]
            {
                (Key.Enter, PhysicalKey.Enter),
                (Key.Space, PhysicalKey.Space),
            })
            {
                walkthrough.PressKeyOnProjectMenuEntry("Configure the project", key, physicalKey);
                Assert.True(setup.IsOpen, $"pressing {key} on Configure did not reopen setup");
                Assert.True(walkthrough.SetupDialogIsShown, $"pressing {key} opened setup, but it is not on screen");
                Assert.Equal(0, setup.Step);
                Assert.Equal(page, walkthrough.Workspace.CurrentPage);
                walkthrough.SkipSetup();
                Assert.False(setup.IsOpen, $"skipping setup after {key} left it open");
                Assert.False(walkthrough.SetupDialogIsShown, $"skipping setup after {key} left it on screen");
            }

            ClickConfigureAndExpectSetup(walkthrough, "after skipping setup");
            Assert.Equal(SeededProject.TextTitle, Assert.Single(setup.Selection.Texts).Title);
            walkthrough.SkipSetup();
            Assert.False(setup.IsOpen);
            Assert.False(walkthrough.SetupDialogIsShown);

            new FieldWorksSimulator(project.FwDataPath).SaveEdit(_ => { });
            RefreshAndWait(walkthrough, deadline);
            ClickConfigureAndExpectSetup(walkthrough, "after Refresh");
            walkthrough.SkipSetup();
            Assert.False(setup.IsOpen);
            Assert.False(walkthrough.SetupDialogIsShown);

            var openingsBeforeDoubleClick = opened;
            var clicks = walkthrough.DoubleClickProjectMenuEntry("Configure the project");
            Assert.True(clicks >= 1, "the double-click never clicked Configure");
            Assert.Equal(openingsBeforeDoubleClick + 1, opened);
            Assert.True(walkthrough.SetupDialogIsShown, "double-clicking Configure did not leave setup on screen");
            Assert.Equal(0, setup.Step);
            Assert.Equal(page, walkthrough.Workspace.CurrentPage);
            walkthrough.SkipSetup();
            Assert.False(setup.IsOpen);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static void RefreshAndWait(WalkthroughWindow walkthrough, long deadline)
    {
        var baseline = walkthrough.Workspace.Baseline;
        var before = baseline.Token;
        walkthrough.Click("Refresh the project");
        walkthrough.WaitUntil(
            () => baseline.ShownRefusal is not null || (baseline.HasBaseline && baseline.Token != before &&
                !walkthrough.Workspace.RefreshCommand.IsRunning &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted),
            WalkthroughSteps.Remaining(deadline), "the Refresh did not capture a new Baseline");
        Assert.True(baseline.ShownRefusal is null,
            $"the Refresh was refused: {baseline.ShownRefusal?.Code}: {baseline.ShownRefusal?.Details}");
    }

    private static void ClickConfigureAndExpectSetup(WalkthroughWindow walkthrough, string when)
    {
        var setup = walkthrough.Workspace.Context.Setup!;
        var page = walkthrough.Workspace.CurrentPage;
        walkthrough.ConfigureFromProjectMenu();
        Assert.True(setup.IsOpen, $"Configure did not reopen setup {when}");
        Assert.True(walkthrough.SetupDialogIsShown, $"Configure opened setup {when}, but it is not on screen");
        Assert.Equal(0, setup.Step);
        Assert.Equal(page, walkthrough.Workspace.CurrentPage);
    }
}
