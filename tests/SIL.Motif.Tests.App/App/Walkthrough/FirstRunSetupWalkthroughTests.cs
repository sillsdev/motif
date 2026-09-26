using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class FirstRunSetupWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FirstRunSetupAdvancesAndSavesTheSelection()
    {
        using var project = new TwoTextWalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: FakeParser.ExecutablePath);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 2, WalkthroughSteps.Remaining(deadline));

            var setup = walkthrough.Workspace.Context.Setup!;
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            Assert.Equal(1, setup.Step);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Back");
            Assert.Equal(0, setup.Step);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, true);
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, "Second Seeded Text", true);
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, "Second Seeded Text", false);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.TypeSetupLimit(walkthrough, "Time limit per word, in seconds", "2.7");
            SetupWalkthroughActions.TypeSetupLimit(walkthrough, "Parser step limit per word", "3100");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Start first run");
            walkthrough.WaitUntil(
                () => !setup.IsOpen && walkthrough.Workspace.Assess.State == SIL.Motif.App.ViewModels.RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "Finish did not close setup and complete its first run");

            var selectedId = Assert.Single(walkthrough.Workspace.Selection.ChosenTextIds);
            Assert.Equal("1 text, step cap 3,100", walkthrough.Workspace.Selection.SummaryText);
            walkthrough.ShowPage(SIL.Motif.App.ViewModels.WorkspacePage.Texts);
            walkthrough.ShowTextsTab(SIL.Motif.App.ViewModels.TextsTab.AnalyzeTexts);
            Assert.Contains(walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>(), text =>
                text.Text == walkthrough.Workspace.Selection.SummaryText && text.IsVisible);
            Assert.Equal(2.7m, walkthrough.Workspace.Selection.PerWordTimeLimitSeconds);
            Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);

            var commands = Assert.IsType<CommandClient>(walkthrough.Workspace.Context.Commands);
            var stored = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            Assert.Equal(selectedId, Assert.Single(stored.Value!.Selection!.TextIds));
            Assert.Equal(2700, stored.Value.Selection.PerWordLimitMs);
            Assert.Equal(3100, stored.Value.Selection.PerWordStepLimit!.Steps);

            SetupWalkthroughActions.OpenConfigure(walkthrough);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            Assert.True(walkthrough.Workspace.Context.Setup!.Selection.Texts
                .Single(text => text.Title == SeededProject.TextTitle).IsChecked);
            Assert.False(walkthrough.Workspace.Context.Setup.Selection.Texts
                .Single(text => text.Title == "Second Seeded Text").IsChecked);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            Assert.Equal(2.7m, walkthrough.Find<NumericUpDown>("Time limit per word, in seconds").Value);
            Assert.Equal(3100m, walkthrough.Find<NumericUpDown>("Parser step limit per word").Value);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Use this Selection");
            walkthrough.WaitUntil(
                () => !setup.IsOpen,
                WalkthroughSteps.Remaining(deadline), "using the saved Selection did not close setup");
            return;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
