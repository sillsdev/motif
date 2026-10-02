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
        var batchStarted = Path.Combine(project.ManagedRoot, "cancelled-assessment-batch-started");
        var releaseBatch = Path.Combine(project.ManagedRoot, "release-cancelled-assessment-batch");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
            walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                SeededProject.FirstForm, SeededProject.SecondForm));
            InteractiveControlSweep.AssertScene(walkthrough, "ready to assess pasted words",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Occurrence,
                InteractiveControlFamily.Collection);
            var beforeCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
            var setupInvocation = Assert.Single(beforeCancellation);
            Assert.Equal(new[] { SeededProject.AnalysedWordForm, SeededProject.UnanalysedWordForm }
                    .Order(StringComparer.Ordinal),
                setupInvocation.Selection.ResolvedWords.Order(StringComparer.Ordinal));

            var existingParserIds = PanglossProcesses.Snapshot(parserPath);
            FakeParser.BehaveBesideExecutable(parserPath, new
            {
                subcommands = new Dictionary<string, object>
                {
                    ["batch"] = new { startedPath = batchStarted, holdUntilPath = releaseBatch },
                },
            });
            WalkthroughSteps.StartSlowAssessment(walkthrough, deadline,
                [SeededProject.FirstForm, SeededProject.SecondForm]);
            walkthrough.WaitUntil(
                () => File.Exists(batchStarted) &&
                    PanglossProcesses.Snapshot(parserPath).Except(existingParserIds).Any(),
                WalkthroughSteps.Remaining(deadline), "the fake PanGloss process did not reach its held batch");
            InteractiveControlSweep.AssertScene(walkthrough, "Assessment running",
                InteractiveControlFamily.Action,
                InteractiveControlFamily.Link,
                InteractiveControlFamily.Filter,
                InteractiveControlFamily.TextEntry,
                InteractiveControlFamily.Choice,
                InteractiveControlFamily.Check,
                InteractiveControlFamily.Disclosure,
                InteractiveControlFamily.List,
                InteractiveControlFamily.MatrixCell,
                InteractiveControlFamily.SelectableText,
                InteractiveControlFamily.Mark,
                InteractiveControlFamily.Morpheme,
                InteractiveControlFamily.Progress,
                InteractiveControlFamily.FocusableSurface,
                InteractiveControlFamily.Container,
                InteractiveControlFamily.Collection);
            var processId = Assert.Single(PanglossProcesses.Snapshot(parserPath).Except(existingParserIds));

            try
            {
                walkthrough.Click("Cancel parsing all words");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State == RunState.Cancelled &&
                        walkthrough.Workspace.Assess.RunCommand.CanExecute(null) &&
                        walkthrough.Workspace.Assess.RunCommand.ExecutionTask is { IsCompleted: true } &&
                        !PanglossProcesses.AnyAlive(parserPath, [processId]),
                    WalkthroughSteps.Remaining(deadline), "the Assessment cancellation did not complete");
                InteractiveControlSweep.AssertScene(walkthrough, "Assessment cancelled",
                    InteractiveControlFamily.Action,
                    InteractiveControlFamily.Link,
                    InteractiveControlFamily.Filter,
                    InteractiveControlFamily.TextEntry,
                    InteractiveControlFamily.Choice,
                    InteractiveControlFamily.Check,
                    InteractiveControlFamily.Disclosure,
                    InteractiveControlFamily.List,
                    InteractiveControlFamily.MatrixCell,
                    InteractiveControlFamily.SelectableText,
                    InteractiveControlFamily.Mark,
                    InteractiveControlFamily.Morpheme,
                    InteractiveControlFamily.FocusableSurface,
                    InteractiveControlFamily.Container,
                    InteractiveControlFamily.Collection);
                Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                    "the cancelled Assessment left its PanGloss process alive");

                Assert.Equal("assessment.cancelled", walkthrough.Workspace.Assess.Refusal?.Code);
                Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
                Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
                var afterCancellation = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                Assert.Equal(beforeCancellation.Select(invocation => invocation.InvocationId),
                    afterCancellation.Select(invocation => invocation.InvocationId));
            }
            finally
            {
                File.WriteAllText(releaseBatch, string.Empty);
            }

            FakeParser.BehaveBesideExecutable(parserPath, new { });
            var previousRefresh = walkthrough.Workspace.RefreshCommand.ExecutionTask;
            walkthrough.Click("Refresh the project");
            walkthrough.WaitUntil(
                () => !ReferenceEquals(previousRefresh, walkthrough.Workspace.RefreshCommand.ExecutionTask) &&
                    walkthrough.Workspace.RefreshCommand.ExecutionTask is { IsCompleted: true } &&
                    !walkthrough.Workspace.RefreshCommand.IsRunning &&
                    walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Baseline.ShownRefusal is null &&
                    walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
                WalkthroughSteps.Remaining(deadline), "refreshing after cancellation did not publish a Baseline");
            var refresh = Assert.IsAssignableFrom<Task>(walkthrough.Workspace.RefreshCommand.ExecutionTask);
            Assert.NotSame(previousRefresh, refresh);
            refresh.GetAwaiter().GetResult();
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
            Assert.True(baselineToken.HasSameSemanticIdentity(invocation.BaselineToken));
            Assert.Equal(baselineToken.BundleDigest, invocation.BaselineToken.BundleDigest);
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
