using System.Diagnostics;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.App.Walkthrough;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SetupRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public void FinishingSetupSavesTheSelectionAndLimitsAndANewWindowReadsThemBack()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            Guid selectedTextId;
            using (var first = NewWindow(project))
            {
                await SelectProjectAndFinishFirstRun(
                    first, project.FwDataPath, SeededProject.TextTitle, 3100, deadline);
                selectedTextId = Assert.Single(first.Workspace.Selection.ChosenTextIds);
                Assert.True(first.Find<Avalonia.Controls.Button>("Parse all words in the Selection").IsEffectivelyEnabled);
            }

            using var reopened = NewWindow(project);
            reopened.Show();
            reopened.OpenRecentProjectByClick(project.FwDataPath);
            Assert.False(reopened.Workspace.Context.Setup!.IsOpen);
            Assert.Equal(selectedTextId, Assert.Single(reopened.Workspace.Selection.ChosenTextIds));
            Assert.Equal(1m, reopened.Workspace.Selection.PerWordTimeLimitSeconds);
            var commands = Assert.IsType<CommandClient>(reopened.Workspace.Context.Commands);
            var stored = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(stored.Succeeded, stored.Refusal?.Message);
            var selection = stored.Value!.Selection!;
            Assert.Equal(selectedTextId, Assert.Single(selection.TextIds));
            Assert.Equal(1000, selection.PerWordLimitMs);
            Assert.Equal(3100, selection.PerWordStepLimit!.Steps);
            Assert.True(reopened.Find<Avalonia.Controls.Button>("Parse all words in the Selection").IsEffectivelyEnabled);
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void ASkipIsKeptForThatProjectOnly()
    {
        using var projectA = new WalkthroughProject(pristine);
        using var projectB = new WalkthroughProject(pristine);
        var managedRoot = projectB.ManagedRoot;
        var deadline = Stopwatch.GetTimestamp() + 240 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using (var first = NewWindow(projectB, managedRoot))
            {
                SetupWalkthroughActions.SelectProject(first, projectB.FwDataPath);
                SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                    first, 1, WalkthroughSteps.Remaining(deadline));
                first.SkipSetup();
                var firstCommands = Assert.IsType<CommandClient>(first.Workspace.Context.Commands);
                var skipped = await firstCommands.ReadDefaultSelectionAsync(
                    new ReadDefaultSelectionRequest(projectB.FwDataPath), CancellationToken.None);
                Assert.True(skipped.Succeeded, skipped.Refusal?.Message);
                Assert.True(skipped.Value!.SetupSkipped);
                Assert.Null(skipped.Value.Selection);
            }

            using var reopened = NewWindow(projectB, managedRoot);
            reopened.Show();
            reopened.OpenRecentProjectByClick(projectB.FwDataPath);
            Assert.False(reopened.Workspace.Context.Setup!.IsOpen);
            var commands = Assert.IsType<CommandClient>(reopened.Workspace.Context.Commands);
            var skippedAgain = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(projectB.FwDataPath), CancellationToken.None);
            Assert.True(skippedAgain.Succeeded, skippedAgain.Refusal?.Message);
            Assert.True(skippedAgain.Value!.SetupSkipped);

            SetupWalkthroughActions.SelectProject(reopened, projectA.FwDataPath);
            var beforeRefresh = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(projectA.FwDataPath), CancellationToken.None);
            Assert.True(beforeRefresh.Succeeded, beforeRefresh.Refusal?.Message);
            Assert.False(beforeRefresh.Value!.SetupSkipped);
            Assert.Null(beforeRefresh.Value.Selection);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                reopened, 1, WalkthroughSteps.Remaining(deadline));
            Assert.True(reopened.Workspace.Context.Setup!.IsOpen);
        }, WalkthroughSteps.Remaining(deadline));
    }

    [Fact]
    public void ConfigureShowsTheStoredChoicesAndTheNextRunUsesTheNewOnes()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 240 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = NewWindow(project);
            SetupWalkthroughActions.SelectProject(walkthrough, project.FwDataPath);
            SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
                walkthrough, 1, WalkthroughSteps.Remaining(deadline));
            SetupWalkthroughActions.FinishFirstRun(
                walkthrough, SeededProject.TextTitle, "3100",
                WalkthroughSteps.Remaining(deadline));

            var commands = Assert.IsType<CommandClient>(walkthrough.Workspace.Context.Commands);
            var original = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(original.Succeeded, original.Refusal?.Message);
            Assert.Equal(1000, original.Value!.Selection!.PerWordLimitMs);

            SetupWalkthroughActions.OpenConfigure(walkthrough);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            Assert.True(walkthrough.Workspace.Context.Setup!.Selection.Texts.Single().IsChecked);
            Assert.Equal(SeededProject.TextTitle,
                Assert.Single(walkthrough.Workspace.Context.Setup.Selection.Texts).Title);
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            Assert.Equal(3100m, walkthrough.Find<Avalonia.Controls.NumericUpDown>(
                "Parser step limit per word").Value);
            Assert.Contains("At this limit a word takes up to about",
                walkthrough.Workspace.Context.Setup!.StepLimitEstimateText, StringComparison.Ordinal);

            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Back");
            SetupWalkthroughActions.SetSetupTextChecked(walkthrough, SeededProject.TextTitle, false);
            walkthrough.Type("Words to add", "motifb");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.TypeSetupLimit(walkthrough, "Parser step limit per word", "6600");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Use this Selection");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Context.Setup?.IsOpen == false,
                WalkthroughSteps.Remaining(deadline), "Configure did not save and close the changed Selection");

            var changed = await commands.ReadDefaultSelectionAsync(
                new ReadDefaultSelectionRequest(project.FwDataPath), CancellationToken.None);
            Assert.True(changed.Succeeded, changed.Refusal?.Message);
            Assert.Empty(changed.Value!.Selection!.TextIds);
            Assert.Equal("motifb", Assert.Single(changed.Value.Selection.AddedWords));
            Assert.Equal(6600, changed.Value.Selection.PerWordStepLimit!.Steps);
            var rate = SelectionLimitEstimateQuery.ReadParserStepRate(project.FwDataPath);
            Assert.True(rate.Succeeded, rate.Refusal?.Message);
            Assert.Equal(StepLimitEstimator.Calculate(new StepCap(6600), rate.Value!)!.PerWordTimeLimitMs,
                changed.Value.Selection.PerWordLimitMs);

            var previousInvocation = walkthrough.Workspace.Assess.Result!.InvocationId;
            walkthrough.Click("Parse all words in the Selection");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Assess.Result?.InvocationId is { } invocationId &&
                    invocationId != previousInvocation &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the next run did not complete from the changed Selection");

            var current = await commands.ReadCurrentEvidenceAsync(project.FwDataPath, CancellationToken.None);
            Assert.True(current.Succeeded, current.Refusal?.Message);
            var matchingAssessment = current.Value!.MatchingAssessment;
            Assert.NotNull(matchingAssessment);
            Assert.Empty(current.Value!.DefaultSelection!.TextIds);
            Assert.Equal("motifb", Assert.Single(current.Value.DefaultSelection.AddedWords));
            Assert.Equal("motifb", Assert.Single(current.Value.Selection!.Selection.Words));
            Assert.Equal(changed.Value.Selection.PerWordLimitMs, matchingAssessment!.Invocation!.PerWordTimeoutMs);
            Assert.Equal(6600, matchingAssessment.Invocation.PerWordStepLimit.Steps);
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static WalkthroughWindow NewWindow(WalkthroughProject project) =>
        NewWindow(project, project.ManagedRoot);

    private static WalkthroughWindow NewWindow(WalkthroughProject project, string managedRoot)
    {
        var parserPath = Path.Combine(managedRoot, FakeParser.ExecutableFileName);
        if (!File.Exists(parserPath))
            parserPath = FakeParser.Copy(managedRoot);
        return new(managedRoot, project.FwDataPath, parserPath: parserPath);
    }

    private static async Task SelectProjectAndFinishFirstRun(
        WalkthroughWindow walkthrough, string projectPath, string selectedText,
        long stepLimit, long deadline)
    {
        SetupWalkthroughActions.SelectProject(walkthrough, projectPath);
        SetupWalkthroughActions.CaptureBaselineAndWaitForSetup(
            walkthrough, 1, WalkthroughSteps.Remaining(deadline));
        SetupWalkthroughActions.FinishFirstRun(
            walkthrough, selectedText, stepLimit.ToString(System.Globalization.CultureInfo.CurrentCulture),
            WalkthroughSteps.Remaining(deadline));
        var commands = Assert.IsType<CommandClient>(walkthrough.Workspace.Context.Commands);
        var stored = await commands.ReadDefaultSelectionAsync(
            new ReadDefaultSelectionRequest(projectPath), CancellationToken.None);
        Assert.True(stored.Succeeded, stored.Refusal?.Message);
        Assert.Single(stored.Value!.Selection!.TextIds);
        Assert.Equal(1000, stored.Value.Selection.PerWordLimitMs);
        Assert.Equal(stepLimit, stored.Value.Selection.PerWordStepLimit!.Steps);
    }
}
