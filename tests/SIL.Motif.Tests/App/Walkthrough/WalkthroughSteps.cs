using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughSteps
{
    internal static void ChooseProjectAndCaptureBaseline(WalkthroughWindow walkthrough, long deadline)
    {
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
    }

    internal static TimeSpan Remaining(long deadline)
    {
        var ticks = deadline - Stopwatch.GetTimestamp();
        return ticks > 0 ? TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency) : TimeSpan.Zero;
    }

    internal static void RunAssessmentOverPastedWords(WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Type("Pasted words", "motifa\nmotifb\nmofita");
        Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);

        walkthrough.Click("Run the Assessment");
        Assert.False(walkthrough.Window.FindControl<ContentControl>("ProjectHost")!.IsEffectivelyEnabled);
        Assert.False(walkthrough.Window.FindControl<ContentControl>("SelectionHost")!.IsEffectivelyEnabled);
        Assert.True(walkthrough.Find<Button>("Cancel the running Assessment").IsEffectivelyEnabled);

        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == AssessRunState.Completed,
            Remaining(deadline), "the Assessment did not complete");
    }
}
