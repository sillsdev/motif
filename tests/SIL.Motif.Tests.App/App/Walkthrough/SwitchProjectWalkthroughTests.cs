using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

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
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline, firstProject.Words, () =>
            {
                var progress = walkthrough.Workspace.Assess.Progress!;
                Assert.Equal(firstProject.Words.Count, progress.Total);
                Assert.InRange(progress.Completed, 0, firstProject.Words.Count - 1);
                Assert.False(walkthrough.ProjectSwitchEnabled);
                Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
                Assert.True(walkthrough.Find<Button>("Cancel parsing all words").IsEffectivelyEnabled);
                PanglossProcesses.TrackNew(parserPath, existing, appeared);
                walkthrough.Workspace.Assess.CancelCommand.Execute(null);
            });
            Assert.NotEmpty(appeared);
            Assert.Equal(firstProject.Words.Count, walkthrough.Workspace.Selection.PastedWordEntries.Count);
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(parserPath, existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Cancelling ||
                    walkthrough.Workspace.Assess.State == RunState.Cancelled;
            }, WalkthroughSteps.Remaining(deadline), "the Assessment cancellation was not requested");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(parserPath, existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Cancelled &&
                    !PanglossProcesses.AnyAlive(parserPath, appeared);
            }, WalkthroughSteps.Remaining(deadline), "the first Assessment did not cancel");
            Assert.True(walkthrough.ProjectSwitchEnabled);

            walkthrough.ProjectPath = secondProject.FwDataPath;
            walkthrough.ChooseNewProject();
            walkthrough.WaitUntil(() =>
                walkthrough.Workspace.Assess.State == RunState.Idle &&
                    walkthrough.Workspace.Assess.Result is null &&
                    walkthrough.Workspace.Assess.Refusal is null &&
                    !walkthrough.Workspace.Context.HasEvidence &&
                    walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1 &&
                    walkthrough.Workspace.Context.Setup?.IsOpen == true &&
                    walkthrough.Workspace.Context.Setup.ConfigurationLoadTask?.IsCompleted != false,
                WalkthroughSteps.Remaining(deadline), "opening the second project did not clear the run and capture its Baseline");
            walkthrough.SkipSetup();
            Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal);
            walkthrough.WaitUntil(
                () => !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled,
                WalkthroughSteps.Remaining(deadline), "the second project's Text selection did not become ready");
            walkthrough.Check(SeededProject.TextTitle);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
            Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
