using System.Diagnostics;
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
                var baselineToken = walkthrough.Workspace.Baseline.Token;
                WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = walkthrough.Workspace.Assess.Result!.InvocationId;

                walkthrough.Click("Write the AI Handoff folder");
                var handoffDeadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.State == RunState.Completed,
                    WalkthroughSteps.Remaining(handoffDeadline), "the Handoff did not complete");

                var handoff = walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff;
                Assert.Equal(Path.GetFullPath(outputDirectory), Path.GetFullPath(handoff.OutputDirectory!));
                Assert.NotNull(handoff.Result);
                Assert.Equal(baselineToken, handoff.Result!.Baseline.Token);
                Assert.Equal(HandoffFileExpectations.Assessed.Select(file => file.RelativePath),
                    handoff.Files.Select(file => file.RelativePath));
                Assert.All(handoff.Files, file =>
                {
                    Assert.True(File.Exists(file.FullPath), file.FullPath);
                    Assert.StartsWith(Path.GetFullPath(outputDirectory) + Path.DirectorySeparatorChar,
                        Path.GetFullPath(file.FullPath), StringComparison.OrdinalIgnoreCase);
                });

                var receiver = new FakeChatReceiver(output);
                walkthrough.DragAllFiles();
                receiver.Drop(walkthrough.DraggedPaths);
                Assert.Equal(HandoffFileExpectations.Assessed.Select(file => Path.GetFileName(file.RelativePath)),
                    receiver.Files.Keys.Order(StringComparer.Ordinal));

                walkthrough.Click("Copy the starter prompt");
                var clipboard = walkthrough.Window.Clipboard
                    ?? throw new InvalidOperationException("The walkthrough window has no clipboard.");
                receiver.Paste(await clipboard.TryGetTextAsync()
                    ?? throw new InvalidOperationException("The starter prompt was not on the clipboard."));

                var validation = receiver.Validate();
                Assert.Empty(validation.Failures);
                Assert.Empty(validation.Findings);
                Assert.Equal(assessmentInvocationId, walkthrough.Workspace.PageModel<AiHandoffPageModel>().Handoff.Result!.InvocationId);
                Assert.Equal(retainedBeforeHandoff.Count,
                    WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
                Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            WalkthroughTestFiles.DeleteDirectory(handoffParent);
        }
    }

}
