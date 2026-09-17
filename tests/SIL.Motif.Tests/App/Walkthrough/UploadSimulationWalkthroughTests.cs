using System.Diagnostics;
using System.Security.Cryptography;
using Xunit;
using Xunit.Abstractions;
using Avalonia.Input.Platform;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class UploadSimulationWalkthroughTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    [RealParserFact]
    public void ACompletedHandoffSurvivesFlatUploadAndStarterPromptPaste()
    {
        using var project = new WalkthroughProject(pristine);
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.UploadWalkthrough", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");

        try
        {
            var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;
            AvaloniaHeadlessFixture.RunUntilComplete(async () =>
            {
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory);
                var baselineDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, baselineDeadline);
                walkthrough.Check(SeededProject.TextTitle);
                WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = walkthrough.Workspace.Assess.Result!.InvocationId;

                walkthrough.Click("Write the Handoff folder");
                var handoffDeadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Completed,
                    WalkthroughSteps.Remaining(handoffDeadline), "the Handoff did not complete");

                var receiver = new FakeChatReceiver(output);
                walkthrough.DragAllFiles();
                receiver.Drop(walkthrough.DraggedPaths);

                walkthrough.Click("Copy the starter prompt");
                var clipboard = walkthrough.Window.Clipboard
                    ?? throw new InvalidOperationException("The walkthrough window has no clipboard.");
                receiver.Paste(await clipboard.TryGetTextAsync()
                    ?? throw new InvalidOperationException("The starter prompt was not on the clipboard."));

                var validation = receiver.Validate();
                Assert.Empty(validation.Failures);
                Assert.Equal(assessmentInvocationId, walkthrough.Workspace.Handoff.Result!.InvocationId);
                Assert.Equal(retainedBeforeHandoff.Count,
                    WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
                Assert.Equal(project.SourceSha256, Sha256(project.FwDataPath));
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            if (Directory.Exists(handoffParent)) Directory.Delete(handoffParent, recursive: true);
        }
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
