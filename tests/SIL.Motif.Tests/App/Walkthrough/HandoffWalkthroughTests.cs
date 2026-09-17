using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandoffWalkthroughTests(PristineProjectFixture pristine)
{
    [RealParserFact]
    public void WritingHandoffPublishesFilesForCompletedAssessment()
    {
        using var project = new WalkthroughProject(pristine);
        var handoffParent = Path.Combine(
            Path.GetTempPath(), "SIL.Motif.Walkthrough", Guid.NewGuid().ToString("N"));
        var outputDirectory = Path.Combine(handoffParent, "handoff");

        try
        {
            var deadline = Stopwatch.GetTimestamp() + 360 * Stopwatch.Frequency;
            AvaloniaHeadlessFixture.RunUntilComplete(() =>
            {
                using var walkthrough = new WalkthroughWindow(
                    project.ManagedRoot, project.FwDataPath, outputDirectory);
                var baselineDeadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;
                WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, baselineDeadline);
                walkthrough.Check(SeededProject.TextTitle);

                WalkthroughSteps.RunAssessmentOverPastedWords(walkthrough, deadline);
                var retainedBeforeHandoff = WalkthroughStoreAssertions.ListInvocations(project.FwDataPath);
                var assessmentInvocationId = walkthrough.Workspace.Assess.Result!.InvocationId;

                Assert.NotNull(walkthrough.Workspace.Baseline.Token);
                var baselineToken = walkthrough.Workspace.Baseline.Token!;
                walkthrough.Click("Write the Handoff folder");
                var handoffDeadline = Stopwatch.GetTimestamp() + 180 * Stopwatch.Frequency;
                walkthrough.WaitUntil(
                    () => walkthrough.Workspace.Handoff.State == RunState.Completed,
                    WalkthroughSteps.Remaining(handoffDeadline), "the Handoff did not complete");

                var handoff = walkthrough.Workspace.Handoff;
                Assert.Equal(Path.GetFullPath(outputDirectory), Path.GetFullPath(handoff.OutputDirectory!));
                Assert.NotEmpty(handoff.Files);
                var relativePaths = handoff.Files.Select(file => file.RelativePath).ToList();
                Assert.Contains("grammar.json", relativePaths);
                Assert.Contains("instructions.md", relativePaths);

                var outputPrefix = Path.GetFullPath(outputDirectory) + Path.DirectorySeparatorChar;
                Assert.All(handoff.Files, file =>
                {
                    Assert.True(File.Exists(file.FullPath), file.FullPath);
                    Assert.StartsWith(outputPrefix, Path.GetFullPath(file.FullPath),
                        StringComparison.OrdinalIgnoreCase);
                });

                Assert.Equal(baselineToken, handoff.Result!.Baseline.Token);
                Assert.Equal(assessmentInvocationId, handoff.Result.InvocationId);
                Assert.Equal(retainedBeforeHandoff.Count,
                    WalkthroughStoreAssertions.ListInvocations(project.FwDataPath).Count);
                walkthrough.DragAllFiles();
                Assert.All(handoff.Files, file => Assert.Contains(file.FullPath, walkthrough.DraggedPaths));
                Assert.Equal(project.SourceSha256, Sha256(project.FwDataPath));

                return Task.CompletedTask;
            }, WalkthroughSteps.Remaining(deadline));
        }
        finally
        {
            if (Directory.Exists(handoffParent)) Directory.Delete(handoffParent, recursive: true);
        }
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
