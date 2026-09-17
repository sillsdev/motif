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
    // Project controls are disabled during a run, so a switch is cancel first, then Browse, then nothing left.
    [RealParserFact]
    public void BrowseIsDisabledDuringARunAndSwitchingAfterCancelClearsTheFirstProject()
    {
        using var firstProject = new ConformanceProject();
        using var secondProject = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                firstProject.ManagedRoot, firstProject.FwDataPath);
            WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);

            var existing = PanglossProcesses.Snapshot();
            var appeared = new HashSet<int>();
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline);
            PanglossProcesses.TrackNew(existing, appeared);

            Assert.False(walkthrough.Find<Button>("Browse for a FieldWorks project file").IsEffectivelyEnabled);
            Assert.False(walkthrough.Find<ComboBox>("Known projects").IsEffectivelyEnabled);

            walkthrough.Click("Cancel the running Assessment");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Cancelled;
            }, WalkthroughSteps.Remaining(deadline), "the first Assessment did not cancel");
            Assert.True(walkthrough.Find<Button>("Browse for a FieldWorks project file").IsEffectivelyEnabled);

            walkthrough.ProjectPath = secondProject.FwDataPath;
            walkthrough.Click("Browse for a FieldWorks project file");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Idle &&
                    walkthrough.Workspace.Assess.Result is null &&
                    walkthrough.Workspace.Assess.Refusal is null &&
                    !walkthrough.Workspace.HasEverAssessed &&
                    walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                    walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.";
            }, WalkthroughSteps.Remaining(deadline), "browsing to the second project did not clear the first run");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(existing, appeared);
                return !PanglossProcesses.AnyAlive(appeared);
            }, TimeSpan.FromSeconds(10), "switching projects left the first PanGloss process alive");

            Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
            Assert.Equal("Capture a Baseline to choose Texts.",
                walkthrough.Workspace.Selection.TextsEmptyMessage);

            walkthrough.Click("Refresh the Baseline");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1,
                WalkthroughSteps.Remaining(deadline), "the second project Baseline did not complete");
            walkthrough.Check(SeededProject.TextTitle);
            WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
            Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
