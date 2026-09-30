using System.Diagnostics;
using Avalonia.Controls;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Tests.Parser;
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
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: FakeParser.ExecutablePath);
            WalkthroughSteps.ChooseProjectAndCaptureBaseline(walkthrough, deadline);

            walkthrough.ConfigureFromProjectMenu();
            SetupWalkthroughActions.FinishFirstRun(
                walkthrough, SeededProject.TextTitle, StepCap.DefaultSteps.ToString(),
                WalkthroughSteps.Remaining(deadline));
            Assert.False(walkthrough.Workspace.Context.NeedsAssessment);
            Assert.True(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);
            Assert.True(walkthrough.Find<Button>("Write the AI Handoff folder").IsEffectivelyEnabled);
            Assert.Equal($"1 text, step cap {StepCap.DefaultSteps:N0}", walkthrough.Workspace.Selection.SummaryText);

            Assert.Equal(project.SourceSha256, WalkthroughStoreAssertions.Sha256(project.FwDataPath));
            var databasePath = Path.Combine(
                Path.GetDirectoryName(project.FwDataPath)!,
                Path.GetFileNameWithoutExtension(project.FwDataPath) + ".motif.db");
            Assert.True(File.Exists(databasePath));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "captures")));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "baselines")));

            return Task.CompletedTask;
        }, WalkthroughSteps.Remaining(deadline));
    }

}
