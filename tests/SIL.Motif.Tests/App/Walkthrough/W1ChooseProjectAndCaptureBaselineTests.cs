using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
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
            walkthrough.Show();

            Assert.Equal(0, walkthrough.Find<ComboBox>("Known projects").ItemCount);
            Assert.True(walkthrough.Find<Button>("Browse for a FieldWorks project file").IsEffectivelyEnabled);

            walkthrough.Click("Browse for a FieldWorks project file");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                    walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
                Remaining(deadline), "choosing the project did not load its initial window state");
            Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
            Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);
            Assert.True(walkthrough.Find<Button>("Refresh the Baseline").IsEffectivelyEnabled);
            Assert.False(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            Assert.False(walkthrough.Find<Button>("Write the Handoff folder").IsEffectivelyEnabled);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("ProjectHost")!.IsEffectivelyEnabled);
            Assert.True(walkthrough.Window.FindControl<ContentControl>("SelectionHost")!.IsEffectivelyEnabled);

            walkthrough.Click("Refresh the Baseline");
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Baseline.HasBaseline &&
                    walkthrough.Workspace.Selection.Texts.Count == 1,
                Remaining(deadline), "refreshing the Baseline did not publish its Texts");
            Assert.NotEqual("No Baseline captured yet", walkthrough.Workspace.Baseline.CapturedTimeText);
            var freshness = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>().Single(text =>
                text.Text == BaselineViewModel.FreshnessSentence);
            Assert.True(freshness.IsVisible);
            Assert.Equal("FieldWorks does not currently hold this project.",
                walkthrough.Workspace.Baseline.HeldStatusText);
            Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
            Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);
            Assert.Equal(SeededProject.TextTitle, Assert.Single(walkthrough.Workspace.Selection.Texts).Title);

            walkthrough.Check(SeededProject.TextTitle);
            Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
            Assert.True(walkthrough.Find<Button>("Write the Handoff folder").IsEffectivelyEnabled);
            Assert.Equal("1 text", walkthrough.Workspace.Selection.SummaryText);

            Assert.Equal(project.SourceSha256, Sha256(project.FwDataPath));
            var databasePath = Path.Combine(
                Path.GetDirectoryName(project.FwDataPath)!,
                Path.GetFileNameWithoutExtension(project.FwDataPath) + ".motif.db");
            Assert.True(File.Exists(databasePath));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "captures")));
            Assert.True(Directory.Exists(Path.Combine(project.ManagedRoot, "baselines")));

            return Task.CompletedTask;
        }, Remaining(deadline));
    }

    private static TimeSpan Remaining(long deadline)
    {
        var ticks = deadline - Stopwatch.GetTimestamp();
        return ticks > 0 ? TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency) : TimeSpan.Zero;
    }

    private static string Sha256(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
