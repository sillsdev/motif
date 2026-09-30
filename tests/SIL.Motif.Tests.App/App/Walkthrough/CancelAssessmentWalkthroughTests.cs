using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelAssessmentWalkthroughTests
{
    [Fact]
    public void CancellingAssessmentLeavesNoInvocationAndAllowsARerun()
    {
        using var project = new ConformanceProject();
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var heartbeat = Path.Combine(project.ManagedRoot, "cancelled-assessment-heartbeat");
        var processIdPath = Path.Combine(project.ManagedRoot, "cancelled-assessment-process-id");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);
            FakeParser.BehaveBesideExecutable(parserPath, new { heartbeatPath = heartbeat, processIdPath });
            var baselineToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                walkthrough.Workspace.Baseline.Token);

            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline);
            walkthrough.WaitUntil(
                () => File.Exists(heartbeat) && File.Exists(processIdPath),
                WalkthroughSteps.Remaining(deadline), "the fake PanGloss process did not reach its heartbeat");
            var processId = int.Parse(File.ReadAllText(processIdPath));

            walkthrough.Click("Cancel the running Assessment");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Cancelled &&
                    walkthrough.Workspace.Assess.RunCommand.CanExecute(null) &&
                    !PanglossProcesses.AnyAlive(parserPath, [processId]),
                WalkthroughSteps.Remaining(deadline), "the Assessment cancellation did not complete");
            Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                "the cancelled Assessment left its PanGloss process alive");

            Assert.Equal("assessment.cancelled", walkthrough.Workspace.Assess.Refusal?.Code);
            Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
            Assert.Empty(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));

            FakeParser.BehaveBesideExecutable(parserPath, new { });
            walkthrough.TypePastedWords(ConformanceProject.OneAnalysisShort);
            walkthrough.Click("Run the Assessment");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the rerun after cancellation did not complete");

            var invocation = Assert.Single(WalkthroughStoreAssertions.ListInvocations(project.FwDataPath));
            Assert.Equal(baselineToken, invocation.BaselineToken);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
