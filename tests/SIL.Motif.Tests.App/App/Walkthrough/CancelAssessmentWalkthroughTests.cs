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
    public void CancellingAssessmentAddsNoInvocationAndAllowsARerun()
    {
        using var project = new ConformanceProject();
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);

            var existing = PanglossProcesses.Snapshot();
            var appeared = new HashSet<int>();
            walkthrough.TypePastedWords(string.Join(Environment.NewLine, ConformanceProject.SlowWords));
            var beforeCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            var setupInvocation = Assert.Single(beforeCancellation);
            Assert.Equal(["motifa"], setupInvocation.Selection.ResolvedWords);
            Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            walkthrough.Click("Run the Assessment");
            Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Running &&
                    walkthrough.Find<Button>("Cancel the running Assessment").IsEffectivelyEnabled,
                WalkthroughSteps.Remaining(deadline), "the slow Assessment did not reach its cancellable Running state");
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
            Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
            var afterCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(beforeCancellation.Select(invocation => invocation.InvocationId),
                afterCancellation.Select(invocation => invocation.InvocationId));

            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Baseline.ShownRefusal is null,
                WalkthroughSteps.Remaining(deadline), "refreshing after cancellation did not publish a Baseline");
            Assert.NotNull(walkthrough.Workspace.Baseline.Token);
            var baselineToken = walkthrough.Workspace.Baseline.Token!;

            Assert.True(walkthrough.Workspace.Assess.RunCommand.CanExecute(null));
            var rerun = walkthrough.Workspace.Assess.RunCommand.ExecuteAsync(null);
            walkthrough.WaitUntil(
                () => rerun.IsCompleted && walkthrough.Workspace.Assess.State == RunState.Completed &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "the Assessment rerun after cancellation did not complete");

            var afterRerun = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(beforeCancellation.Count + 1, afterRerun.Count);
            var invocation = Assert.Single(afterRerun, record =>
                beforeCancellation.All(existingRecord => existingRecord.InvocationId != record.InvocationId));
            Assert.Equal(ConformanceProject.SlowWords.Order(StringComparer.Ordinal),
                invocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));
            Assert.Equal(baselineToken, invocation.BaselineToken);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
