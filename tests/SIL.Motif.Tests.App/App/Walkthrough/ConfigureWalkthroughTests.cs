using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

/// <summary>The project menu's Configure… reopens setup over the window, clicked as a person clicks it.</summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class ConfigureWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ConfigureReopensSetupAfterSkipAndRefresh()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var setup = walkthrough.Workspace.Context.Setup!;
            Assert.False(walkthrough.SetupDialogIsShown);

            ConfigureAndExpectSetup(walkthrough, "after skipping setup");
            Assert.Equal(SeededProject.TextTitle, Assert.Single(setup.Selection.Texts).Title);
            walkthrough.SkipSetup();

            RefreshAfterAFieldWorksSave(walkthrough, project, deadline);
            ConfigureAndExpectSetup(walkthrough, "after a Refresh");
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void ConfigureOpensFromTheKeyboard()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var setup = walkthrough.Workspace.Context.Setup!;

            foreach (var (key, physicalKey) in new[] { (Key.Enter, PhysicalKey.Enter), (Key.Space, PhysicalKey.Space) })
            {
                walkthrough.PressKeyOnProjectMenuEntry("Configure the project", key, physicalKey);
                Assert.True(setup.IsOpen, $"pressing {key} on Configure… did not reopen setup");
                Assert.True(walkthrough.SetupDialogIsShown, $"pressing {key} opened setup, but it is not on screen");
                Assert.Equal(0, setup.Step);
                walkthrough.SkipSetup();
            }
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void DoubleClickingConfigureOpensSetupOnce()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            var setup = walkthrough.Workspace.Context.Setup!;
            var opened = 0;
            setup.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SetupViewModel.IsOpen) && setup.IsOpen) opened++;
            };

            var clicks = walkthrough.DoubleClickProjectMenuEntry("Configure the project");

            Assert.True(clicks >= 1, "the double-click never clicked Configure…");
            Assert.Equal(1, opened);
            Assert.True(walkthrough.SetupDialogIsShown, "double-clicking Configure… did not leave setup on screen");
            Assert.Equal(0, setup.Step);
            walkthrough.SkipSetup();
            Assert.False(setup.IsOpen);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void ConfigureShowsTheSavedSelectionAfterFinishARefreshAndARestart()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 240 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using (var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                       parserPath: FakeParser.ExecutablePath))
            {
                ChooseProjectAndFinishSetup(walkthrough, deadline);

                ConfigureAndExpectSavedSelection(walkthrough, "after Finish");
                walkthrough.SkipSetup();

                RefreshAfterAFieldWorksSave(walkthrough, project, deadline);
                ConfigureAndExpectSavedSelection(walkthrough, "after a Refresh");
                walkthrough.SkipSetup();
            }

            using var restarted = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            restarted.Show();
            restarted.SelectKnownProject(project.FwDataPath);
            restarted.WaitUntil(
                () => restarted.Workspace.Baseline.HasBaseline && restarted.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline), "reopening from Open recent did not reload the Baseline");
            Assert.False(restarted.SetupDialogIsShown, "a project with a saved Selection reopened setup by itself");
            ConfigureAndExpectSavedSelection(restarted, "after reopening from Open recent");
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void BeforeTheFirstRefreshConfigureIsUnavailableAndSaysToRefreshFirst()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            walkthrough.Show();
            walkthrough.ClickProjectMenuEntry("Select a new project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.HasProject &&
                    walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                WalkthroughSteps.Remaining(deadline), "choosing the project did not finish opening it");

            walkthrough.OpenProjectMenu();
            var entry = walkthrough.FindProjectMenuEntry<Button>("Configure the project");
            Assert.False(entry.IsEffectivelyEnabled, "Configure… is offered but can do nothing before a Refresh");
            Assert.Contains(entry.GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == WorkspaceShellViewModel.ConfigureNeedsBaselineText && text.IsEffectivelyVisible);
            walkthrough.CloseProjectMenu();

            RefreshAndWait(walkthrough, deadline);
            walkthrough.SkipSetup();
            walkthrough.OpenProjectMenu();
            Assert.Contains(walkthrough.FindProjectMenuEntry<Button>("Configure the project")
                    .GetLogicalDescendants().OfType<TextBlock>(),
                text => text.Text == "Texts, added words and limits" && text.IsEffectivelyVisible);
            walkthrough.CloseProjectMenu();
            ConfigureAndExpectSetup(walkthrough, "after the first Refresh");
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void OpenRecentClosesTheProjectMenu()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using (var first = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                       parserPath: FakeParser.ExecutablePath))
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(first, deadline);

            using var restarted = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath,
                parserPath: FakeParser.ExecutablePath);
            restarted.Show();
            restarted.SelectKnownProject(project.FwDataPath);
            Assert.Equal(project.FwDataPath, restarted.Workspace.Context.ProjectPath);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private const string AddedWord = "motifa";

    private static void ChooseProjectAndFinishSetup(WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Show();
        walkthrough.ChooseNewProject();
        walkthrough.WaitUntil(() => walkthrough.Workspace.HasProject && !walkthrough.Workspace.Baseline.HasBaseline,
            WalkthroughSteps.Remaining(deadline), "choosing the project did not open it");
        walkthrough.Click("Refresh the project");
        var setup = walkthrough.Workspace.Context.Setup!;
        walkthrough.WaitUntil(
            () => setup.IsOpen && walkthrough.Workspace.Selection.Texts.Count == 1 &&
                !walkthrough.Workspace.RefreshCommand.IsRunning,
            WalkthroughSteps.Remaining(deadline), "the first Refresh did not open setup");

        setup.NextCommand.Execute(null);
        walkthrough.Workspace.Selection.Texts[0].IsChecked = true;
        walkthrough.Type("Words to add", AddedWord);
        setup.NextCommand.Execute(null);
        walkthrough.Workspace.Selection.PerWordTimeLimitSeconds = 2.5m;
        setup.StepLimitSteps = 4321;
        setup.NextCommand.Execute(null);
        walkthrough.Click("Start first run");
        walkthrough.WaitUntil(
            () => !setup.IsOpen && walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
            WalkthroughSteps.Remaining(deadline), "Finish did not save the Selection and complete the first run");
    }

    // A save gives the Refresh a new Baseline folder, clear of the files the fake parser left in the old one.
    private static void RefreshAfterAFieldWorksSave(WalkthroughWindow walkthrough, WalkthroughProject project, long deadline)
    {
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(_ => { });
        RefreshAndWait(walkthrough, deadline);
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
        walkthrough.Workspace.DismissRerunCommand.Execute(null);
    }

    private static void ConfigureAndExpectSetup(WalkthroughWindow walkthrough, string when)
    {
        var setup = walkthrough.Workspace.Context.Setup!;
        var page = walkthrough.Workspace.CurrentPage;
        walkthrough.ConfigureFromProjectMenu();
        Assert.True(setup.IsOpen, $"Configure… did not reopen setup {when}");
        Assert.True(walkthrough.SetupDialogIsShown, $"Configure… opened setup {when}, but it is not on screen");
        Assert.Equal(0, setup.Step);
        Assert.Equal(page, walkthrough.Workspace.CurrentPage);
    }

    private static void ConfigureAndExpectSavedSelection(WalkthroughWindow walkthrough, string when)
    {
        ConfigureAndExpectSetup(walkthrough, when);
        var selection = walkthrough.Workspace.Context.Setup!.Selection;
        Assert.True(Assert.Single(selection.Texts).IsChecked, $"the saved Text is not checked {when}");
        Assert.Equal(AddedWord, selection.PastedWords);
        Assert.Equal(2.5m, selection.PerWordTimeLimitSeconds);
        Assert.Equal(4321m, walkthrough.Workspace.Context.Setup.StepLimitSteps);
        Assert.Equal("Use this Selection", walkthrough.Workspace.Context.Setup.FinishButtonText);
    }
}
