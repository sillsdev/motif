using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Sdk;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class SwitchProjectWalkthroughTests(PristineProjectFixture pristine)
{
    // Project selection is disabled during a run, so a switch is cancel first, then Select new.
    [RealParserFact]
    public async Task ProjectMenuIsDisabledDuringARunAndSwitchingAfterCancelClearsTheFirstProject()
    {
        using var firstProject = await SlowParserWalkthroughProject.CreateAsync();
        using var secondProject = new WalkthroughProject(pristine);
        var parserPath = PanglossProcesses.CopyExecutable(firstProject.ManagedRoot);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                firstProject.ManagedRoot, firstProject.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);

            var existing = PanglossProcesses.Snapshot(parserPath);
            var appeared = new HashSet<int>();
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline, firstProject.Words);
            Assert.Equal(firstProject.Words.Count, walkthrough.Workspace.Selection.PastedWordEntries.Count);
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(parserPath, existing, appeared);
                if (walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused)
                    throw new XunitException("The slow Assessment finished before cancellation could be requested.");

                var progress = walkthrough.Workspace.Assess.Progress;
                return walkthrough.Workspace.Assess.State == RunState.Running &&
                    progress is { Stage: AssessmentStage.Parsing, Completed: 0 } &&
                    progress.Total == firstProject.Words.Count;
            }, WalkthroughSteps.Remaining(deadline), "the real parser did not reach Parsing with work still running");

            Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);

            walkthrough.Click("Cancel parsing all words");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(parserPath, existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Cancelled &&
                    !PanglossProcesses.AnyAlive(parserPath, appeared);
            }, WalkthroughSteps.Remaining(deadline), "the first Assessment did not cancel");
            Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);

            walkthrough.ProjectPath = secondProject.FwDataPath;
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(() =>
                walkthrough.Workspace.Assess.State == RunState.Idle &&
                    walkthrough.Workspace.Assess.Result is null &&
                    walkthrough.Workspace.Assess.Refusal is null &&
                    !walkthrough.Workspace.Context.HasEvidence &&
                    walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                    walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                WalkthroughSteps.Remaining(deadline), "browsing to the second project did not clear the first run");
            walkthrough.SkipSetup();
            Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal);
            Assert.Equal("Capture a Baseline to choose Texts.",
                walkthrough.Workspace.Selection.TextsEmptyMessage);

            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true,
                WalkthroughSteps.Remaining(deadline), "the second project Baseline did not show setup");
            walkthrough.SkipSetup();
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled,
                WalkthroughSteps.Remaining(deadline), "the second project's Texts selection did not become ready");
            walkthrough.Check(SeededProject.TextTitle);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
            Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
