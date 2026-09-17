using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelAssessmentWalkthroughTests
{
    [RealParserFact]
    public void CancellingAssessmentLeavesNoInvocationAndAllowsARerun()
    {
        using var project = new ConformanceProject();
        var deadline = Stopwatch.GetTimestamp() + 600 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);

            var existing = PanglossProcesses.Snapshot();
            var appeared = new HashSet<int>();
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline);
            PanglossProcesses.TrackNew(existing, appeared);

            walkthrough.Click("Cancel the running Assessment");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(existing, appeared);
                return walkthrough.Workspace.Assess.State == RunState.Cancelled;
            }, WalkthroughSteps.Remaining(deadline), "the Assessment cancellation did not complete");
            walkthrough.WaitUntil(() =>
            {
                PanglossProcesses.TrackNew(existing, appeared);
                return !PanglossProcesses.AnyAlive(appeared);
            }, TimeSpan.FromSeconds(10), "the cancelled Assessment left a PanGloss process alive");

            Assert.Equal("assessment.cancelled", walkthrough.Workspace.Assess.Refusal?.Code);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("ProjectHost")!.IsEffectivelyEnabled);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("SelectionHost")!.IsEffectivelyEnabled);
            Assert.Empty(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));

            walkthrough.Click("Refresh the Baseline");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Baseline.RefusalMessage is null,
                WalkthroughSteps.Remaining(deadline), "refreshing after cancellation did not publish a Baseline");
            Assert.NotNull(walkthrough.Workspace.Baseline.Token);
            var baselineToken = walkthrough.Workspace.Baseline.Token!;

            walkthrough.Type("Pasted words", ConformanceProject.OneAnalysisShort);
            walkthrough.Click("Run the Assessment");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Completed,
                WalkthroughSteps.Remaining(deadline), "the rerun after cancellation did not complete");

            var invocation = Assert.Single(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));
            Assert.Equal(baselineToken, invocation.BaselineToken);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
