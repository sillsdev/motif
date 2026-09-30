using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelAssessmentWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CancellingAssessmentAddsNoInvocationAndAllowsARerun()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var heartbeat = Path.Combine(project.ManagedRoot, "cancelled-assessment-heartbeat");
        var processIdPath = Path.Combine(project.ManagedRoot, "cancelled-assessment-process-id");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                SeededProject.FirstForm, SeededProject.SecondForm));
            var beforeCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            var setupInvocation = Assert.Single(beforeCancellation);
            Assert.Equal(new[] { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm }
                    .Order(StringComparer.Ordinal),
                setupInvocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));

            FakeParser.BehaveBesideExecutable(parserPath, new { heartbeatPath = heartbeat, processIdPath });
            Assert.True(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);
            walkthrough.Click("Parse all words in the Selection");
            walkthrough.WaitUntil(
                () => File.Exists(heartbeat) && File.Exists(processIdPath) &&
                    walkthrough.Workspace.Assess.State == RunState.Running,
                WalkthroughSteps.Remaining(deadline), "the fake PanGloss process did not reach its heartbeat");
            var processId = int.Parse(File.ReadAllText(processIdPath));
            Assert.True(PanglossProcesses.AnyAlive(parserPath, [processId]));
            Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.True(walkthrough.Find<Button>("Cancel the running Assessment").IsEffectivelyEnabled);

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
            var afterCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            Assert.Equal(beforeCancellation.Select(invocation => invocation.InvocationId),
                afterCancellation.Select(invocation => invocation.InvocationId));

            // Refresh runs a grammar check through this parser, so it must stop behaving as the endless run.
            FakeParser.BehaveBesideExecutable(parserPath, new { });
            // The first Baseline already satisfies HasBaseline, so wait for this Refresh itself to finish.
            var earlierRefresh = walkthrough.Workspace.RefreshCommand.ExecutionTask;
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.RefreshCommand.ExecutionTask is { IsCompleted: true } refresh &&
                    !ReferenceEquals(refresh, earlierRefresh) &&
                    walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Baseline.ShownRefusal is null,
                WalkthroughSteps.Remaining(deadline), "refreshing after cancellation did not publish a Baseline");
            var baselineToken = Assert.IsType<SIL.Motif.Contract.Baselines.BaselineToken>(
                walkthrough.Workspace.Baseline.Token);

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
            Assert.Equal(new[] { SeededProject.FirstForm, SeededProject.SecondForm }.Order(StringComparer.Ordinal),
                invocation.Selection.PastedWords.Order(StringComparer.Ordinal));
            Assert.Equal(new[]
                {
                    SeededProject.FirstForm,
                    SeededProject.AnalysedWordForm,
                    SeededProject.SecondForm,
                    SeededProject.UnanalysedWordForm,
                }.Order(StringComparer.Ordinal),
                invocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));
            Assert.Equal(baselineToken, invocation.BaselineToken);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
