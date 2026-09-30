using System.Diagnostics;
using System.IO;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelHandoffWalkthroughTests(PristineProjectFixture pristine)
{
    [Fact]
    public void CancellingHandoffDoesNotPublishPartialOutputAndRetrySucceeds()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Walkthrough.CancelHandoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");
        var heartbeatPath = Path.Combine(project.ManagedRoot, "handoff-heartbeat");
        var processIdPath = Path.Combine(project.ManagedRoot, "handoff-process-id");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory, parserPath: parserPath);
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
                walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                    SeededProject.FirstForm, SeededProject.SecondForm));
                FakeParser.BehaveBesideExecutable(parserPath, new { });
                WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Refused,
                    WalkthroughSteps.Remaining(deadline), "the Assessment before Handoff did not complete");
                Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
                Assert.Null(walkthrough.Workspace.Assess.Refusal);
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = walkthrough.Workspace.Assess.Result!.InvocationId;

                FakeParser.BehaveBesideExecutable(parserPath, new { heartbeatPath, processIdPath });
                walkthrough.Click("Write the AI Handoff folder");
                walkthrough.WaitUntil(
                    () => File.Exists(heartbeatPath) && File.Exists(processIdPath) &&
                        walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.State == RunState.Running,
                    WalkthroughSteps.Remaining(deadline), "the Handoff importer did not start");
                var processId = int.Parse(File.ReadAllText(processIdPath));
                Assert.True(PanglossProcesses.AnyAlive(parserPath, [processId]));
                Assert.True(walkthrough.Find<Avalonia.Controls.Button>(
                    "Cancel the running AI Handoff").IsEffectivelyEnabled);
                walkthrough.Click("Cancel the running AI Handoff");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.State == RunState.Cancelled &&
                        !PanglossProcesses.AnyAlive(parserPath, [processId]),
                    WalkthroughSteps.Remaining(deadline), "the Handoff cancellation did not unwind");
                Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                    "the cancelled Handoff left its PanGloss process alive");
                Assert.Equal("handoff.cancelled", walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.Refusal?.Code);
                Assert.False(Directory.Exists(outputDirectory));
                AssertNoGrammarFiles(handoffParent);
                Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
                Assert.NotNull(walkthrough.Workspace.Assess.Result);

                FakeParser.BehaveBesideExecutable(parserPath, new { });
                walkthrough.Click("Write the AI Handoff folder");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the retried Handoff did not complete");

                Assert.Equal(RunState.Completed, walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.State);
                Assert.True(File.Exists(Path.Combine(outputDirectory, "grammar.json")));
                Assert.NotEmpty(walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.Files);
                Assert.Equal(assessmentInvocationId, walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.Result!.InvocationId);
                Assert.Equal(retainedBeforeHandoff.Count,
                    WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(handoffParent);
        }
    }

    private static void AssertNoGrammarFiles(string root)
    {
        if (!Directory.Exists(root)) return;
        Assert.Empty(Directory.EnumerateFiles(root, "grammar.json", SearchOption.AllDirectories));
    }
}
