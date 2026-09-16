using System.Diagnostics;
using System.IO;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CancelHandoffWalkthroughTests(ITestOutputHelper output)
{
    [RealParserFact]
    public void CancellingHandoffDoesNotPublishPartialOutputAndRetrySucceeds()
    {
        using var project = new ConformanceProject();
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Conformance.CancelHandoff", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");
        var deadline = Stopwatch.GetTimestamp() + 600 * Stopwatch.Frequency;

        try
        {
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory);
                WalkthroughSteps.ChooseConformanceProjectAndCaptureBaseline(walkthrough, deadline);
                walkthrough.Type(
                    "Pasted words",
                    string.Join(Environment.NewLine,
                        ConformanceProject.OneAnalysisShort,
                        ConformanceProject.OneAnalysisShort,
                        ConformanceProject.OneAnalysisShort));
                walkthrough.Click("Run the Assessment");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Assess.State is AssessRunState.Completed or AssessRunState.Refused,
                    WalkthroughSteps.Remaining(deadline), "the Assessment before Handoff did not complete");
                Assert.Equal(AssessRunState.Completed, walkthrough.Workspace.Assess.State);
                Assert.Null(walkthrough.Workspace.Assess.Refusal);

                walkthrough.Click("Write the Handoff folder");
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == HandoffRunState.Running ||
                        walkthrough.Workspace.Handoff.State == HandoffRunState.Completed ||
                        walkthrough.Workspace.Handoff.State == HandoffRunState.Refused,
                    WalkthroughSteps.Remaining(deadline), "the Handoff did not start or complete");

                if (walkthrough.Workspace.Handoff.State == HandoffRunState.Running)
                {
                    output.WriteLine("Handoff cancellation observed: yes.");
                    walkthrough.Click("Cancel the running Handoff");
                    walkthrough.WaitUntil(
                        () => walkthrough.Workspace.Handoff.State
                            is HandoffRunState.Cancelled or HandoffRunState.Refused or HandoffRunState.Completed,
                        WalkthroughSteps.Remaining(deadline), "the Handoff cancellation did not unwind");
                    Assert.Equal(HandoffRunState.Cancelled, walkthrough.Workspace.Handoff.State);
                    Assert.Equal("handoff.cancelled", walkthrough.Workspace.Handoff.Refusal?.Code);
                    Assert.True(!Directory.Exists(outputDirectory) ||
                        !Directory.EnumerateFileSystemEntries(outputDirectory).Any());
                    AssertNoGrammarFiles(handoffParent);
                    Assert.Equal(AssessRunState.Completed, walkthrough.Workspace.Assess.State);
                    Assert.NotNull(walkthrough.Workspace.Assess.Result);

                    walkthrough.Click("Write the Handoff folder");
                    walkthrough.WaitUntil(
                        () => walkthrough.Workspace.Handoff.State == HandoffRunState.Completed,
                        WalkthroughSteps.Remaining(deadline), "the retried Handoff did not complete");
                }
                else
                {
                    output.WriteLine("Handoff cancellation observed: no; the first Handoff completed before Cancel.");
                }

                Assert.Equal(HandoffRunState.Completed, walkthrough.Workspace.Handoff.State);
                Assert.True(File.Exists(Path.Combine(outputDirectory, "grammar.json")));
                Assert.NotEmpty(walkthrough.Workspace.Handoff.Files);
                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            try { if (Directory.Exists(handoffParent)) Directory.Delete(handoffParent, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void AssertNoGrammarFiles(string root)
    {
        if (!Directory.Exists(root)) return;
        Assert.Empty(Directory.EnumerateFiles(root, "grammar.json", SearchOption.AllDirectories));
    }
}
