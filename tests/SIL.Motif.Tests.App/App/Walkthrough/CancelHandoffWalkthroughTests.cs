using System.Diagnostics;
using System.IO;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelHandoffWalkthroughTests
{
    [RealParserFact]
    public void CancellingHandoffDoesNotPublishPartialOutputAndRetrySucceeds()
    {
        using var project = new ConformanceProject();
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.CancelHandoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");
        var deadline = Stopwatch.GetTimestamp() + 120 * Stopwatch.Frequency;

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                var holdingClient = new HoldingCommandClient(
                    new CommandClient(project.ManagedRoot), holdHandoff: true);
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory, holdingClient);
                WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);
                walkthrough.Type(
                    "Pasted words",
                    string.Join(Environment.NewLine,
                        ConformanceProject.OneAnalysisShort,
                        ConformanceProject.OneAnalysisShort,
                        ConformanceProject.OneAnalysisShort));
                walkthrough.Click("Run the Assessment");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Refused,
                    WalkthroughSteps.Remaining(deadline), "the Assessment before Handoff did not complete");
                Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
                Assert.Null(walkthrough.Workspace.Assess.Refusal);
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = walkthrough.Workspace.Assess.Result!.InvocationId;

                walkthrough.Click("Write the Handoff folder");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Running,
                    WalkthroughSteps.Remaining(deadline), "the Handoff did not reach Running");
                Assert.True(walkthrough.Find<Avalonia.Controls.Button>(
                    "Cancel the running Handoff").IsEffectivelyEnabled);
                walkthrough.Click("Cancel the running Handoff");
                holdingClient.ReleaseHandoff();
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Cancelled,
                    WalkthroughSteps.Remaining(deadline), "the Handoff cancellation did not unwind");
                Assert.Equal("handoff.cancelled", walkthrough.Workspace.Handoff.Refusal?.Code);
                Assert.Equal(FailureReason.Cancelled, walkthrough.Workspace.Handoff.Refusal?.Reason);
                Assert.False(Directory.Exists(outputDirectory));
                AssertNoGrammarFiles(handoffParent);
                Assert.Equal(RunState.Completed, walkthrough.Workspace.Assess.State);
                Assert.NotNull(walkthrough.Workspace.Assess.Result);

                walkthrough.Click("Write the Handoff folder");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Completed,
                    WalkthroughSteps.Remaining(deadline), "the retried Handoff did not complete");

                Assert.Equal(RunState.Completed, walkthrough.Workspace.Handoff.State);
                Assert.True(File.Exists(Path.Combine(outputDirectory, "grammar.json")));
                Assert.NotEmpty(walkthrough.Workspace.Handoff.Files);
                Assert.Equal(assessmentInvocationId, walkthrough.Workspace.Handoff.Result!.InvocationId);
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
