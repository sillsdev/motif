using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class WindowDisposalCancellationTests(PristineProjectFixture pristine)
{
    [Fact]
    public void DisposingWithAnAssessmentInFlightCancelsTheParserWithoutBlockingTheDispatcher()
    {
        using var project = new WalkthroughProject(pristine);
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var heartbeatPath = Path.Combine(project.ManagedRoot, "disposed-assessment-heartbeat");
        var processIdPath = Path.Combine(project.ManagedRoot, "disposed-assessment-process-id");
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: parserPath);
            var workspace = walkthrough.Workspace;
            int processId;
            try
            {
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);
                walkthrough.TypePastedWords(string.Join(Environment.NewLine,
                    SeededProject.FirstForm, SeededProject.SecondForm));
                FakeParser.BehaveBesideExecutable(parserPath, new { heartbeatPath, processIdPath });
                walkthrough.Click("Run the Assessment");
                walkthrough.WaitUntil(
                    () => File.Exists(heartbeatPath) && File.Exists(processIdPath) &&
                        workspace.Assess.State == RunState.Running,
                    WalkthroughSteps.Remaining(deadline), "the parser did not start before window disposal");
                processId = int.Parse(File.ReadAllText(processIdPath));
                Assert.True(PanglossProcesses.AnyAlive(parserPath, [processId]));
            }
            finally
            {
                walkthrough.Dispose();
            }

            Assert.Equal(RunState.Cancelled, workspace.Assess.State);
            Assert.False(PanglossProcesses.AnyAlive(parserPath, [processId]),
                "disposing the window left its in-flight PanGloss process alive");
            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }
}
