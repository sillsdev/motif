using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

[Collection(LcmCacheTestCollection.Name)]
public sealed class W1ChooseProjectAndCaptureBaselineTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ChoosingProjectCapturingBaselineMakesAssessmentRunnable()
    {
        using var project = new WalkthroughProject(pristine);
        var deadline = Stopwatch.GetTimestamp() + 60 * Stopwatch.Frequency;

        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            using var walkthrough = new WalkthroughWindow(project.ManagedRoot, project.FwDataPath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);

            walkthrough.Check(SeededProject.TextTitle);
            Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            Assert.False(walkthrough.Find<Button>("Write the Handoff folder").IsEffectivelyEnabled);
            Assert.Equal("1 text", walkthrough.Workspace.Selection.SummaryText);

            Assert.Equal(project.SourceSha256, Sha256(project.FwDataPath));
            var databasePath = Path.Combine(
                Path.GetDirectoryName(project.FwDataPath)!,
                Path.GetFileNameWithoutExtension(project.FwDataPath) + ".motif.db");
            Assert.True(File.Exists(databasePath));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "captures")));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "baselines")));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
