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

        Assert.Empty(walkthrough.Workspace.Project.KnownProjects);
        walkthrough.OpenProjectMenu();
        Assert.True(walkthrough.FindProjectMenuEntry<Button>("Select a new project").IsEffectivelyEnabled);
        Assert.False(walkthrough.FindProjectMenuEntry<Button>("Open a recent project").IsEffectivelyEnabled);
        walkthrough.ChooseNewProject();
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
            Remaining(deadline), "choosing the project did not load its initial window state");
        Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
        Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);
        Assert.True(walkthrough.Find<Button>("Refresh the project").IsEffectivelyEnabled);
        Assert.False(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);
        Assert.False(walkthrough.Find<Button>("Write the Handoff folder").IsEffectivelyEnabled);
        Assert.True(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
        Assert.True(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);

        walkthrough.Click("Refresh the project");
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.Texts.Count == 1 &&
                walkthrough.Workspace.Context.Setup?.IsOpen == true,
            Remaining(deadline), "refreshing the Baseline did not publish its Texts and setup dialog");
        walkthrough.WaitUntil(
            () => !walkthrough.Workspace.RefreshCommand.IsRunning,
            TimeSpan.FromMinutes(1), "the Baseline and Overview refresh did not finish before choosing words");
        Assert.True(walkthrough.Workspace.Context.Setup?.IsOpen,
            "first-time setup did not open after the Baseline was captured");
        walkthrough.SkipSetup();
        Assert.False(walkthrough.Workspace.Context.Setup?.IsOpen);
        Assert.NotEqual("No Baseline captured yet", walkthrough.Workspace.Baseline.CapturedTimeText);
        // The Overview distinguishes when Motif captured a Baseline from the FieldWorks save it copies.
        var overview = walkthrough.Workspace.PageModel<OverviewPageModel>();
        walkthrough.WaitUntil(
            () => overview.Overview is { BaselineCapturedUtc: not null, BaselineSourceLastWriteUtc: not null },
            Remaining(deadline), "the Overview did not reload the captured Baseline");
        Assert.NotNull(overview.Overview?.BaselineCapturedUtc);
        Assert.NotNull(overview.Overview?.BaselineSourceLastWriteUtc);
        Assert.Equal(overview.Overview?.LastFieldWorksSaveUtc, overview.Overview?.BaselineSourceLastWriteUtc);
        var baselineDetails = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>().Single(text =>
            text.Text == overview.BaselineDetails);
        Assert.True(baselineDetails.IsVisible);
        Assert.Contains("Baseline ", baselineDetails.Text, StringComparison.Ordinal);
        Assert.Contains("saved ", baselineDetails.Text, StringComparison.Ordinal);
        var projectDetails = walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>().Single(text =>
            text.Text == overview.ProjectDetails);
        Assert.True(projectDetails.IsVisible);
        Assert.Contains("last FieldWorks save ", projectDetails.Text, StringComparison.Ordinal);
        Assert.Equal("FieldWorks does not currently hold this project.",
            walkthrough.Workspace.Baseline.HeldStatusText);
        Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
        Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);
        Assert.Equal(SeededProject.TextTitle, Assert.Single(walkthrough.Workspace.Selection.Texts).Title);
    }

    internal static void ChooseConformanceProjectAndCaptureBaseline(
        WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Show();

        Assert.Empty(walkthrough.Workspace.Project.KnownProjects);
        walkthrough.OpenProjectMenu();
        Assert.True(walkthrough.FindProjectMenuEntry<Button>("Select a new project").IsEffectivelyEnabled);
        Assert.False(walkthrough.FindProjectMenuEntry<Button>("Open a recent project").IsEffectivelyEnabled);
        walkthrough.ChooseNewProject();
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
            Remaining(deadline), "choosing the conformance project did not show its initial state");
        Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
        Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);

        walkthrough.Click("Refresh the project");
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.TextsEmptyMessage == "This Baseline has no Texts." &&
                walkthrough.Workspace.Context.Setup?.IsOpen == true,
            Remaining(deadline), "refreshing the conformance project did not show setup");
        walkthrough.WaitUntil(
            () => !walkthrough.Workspace.RefreshCommand.IsRunning,
            TimeSpan.FromMinutes(1), "the Baseline and Overview refresh did not finish before choosing words");
        Assert.True(walkthrough.Workspace.Context.Setup?.IsOpen,
            "first-time setup did not open after the Baseline was captured");
        walkthrough.SkipSetup();
        Assert.False(walkthrough.Workspace.Context.Setup?.IsOpen);
        Assert.Null(walkthrough.Workspace.Baseline.RefusalMessage);
        Assert.Null(walkthrough.Workspace.Selection.RefusalMessage);
        Assert.Empty(walkthrough.Workspace.Selection.Texts);
    }

    internal static TimeSpan Remaining(long deadline)
    {
        var ticks = deadline - Stopwatch.GetTimestamp();
        return ticks > 0 ? TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency) : TimeSpan.Zero;
    }

    internal static void RunAssessmentOverPastedWords(WalkthroughWindow walkthrough, long deadline)
    {
        StartAssessmentOverPastedWords(walkthrough, deadline);
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == RunState.Completed,
            Remaining(deadline), "the Assessment did not complete");
    }

    internal static void StartSlowAssessment(WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Type("Pasted words", string.Join(Environment.NewLine, ConformanceProject.SlowWords));
        Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);

        walkthrough.Click("Run the Assessment");
        Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
        Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == RunState.Running &&
                walkthrough.Find<Button>("Cancel the running Assessment").IsEffectivelyEnabled,
            Remaining(deadline), "the slow Assessment did not reach its cancellable Running state");
    }

    internal static void StartAssessmentOverPastedWords(
        WalkthroughWindow walkthrough, long deadline, HoldingCommandClient? holdingClient = null)
    {
        walkthrough.Type("Pasted words", "motifa\nmotifb\nmofita");
        Assert.True(walkthrough.Find<Button>("Run the Assessment").IsEffectivelyEnabled);

        walkthrough.Click("Run the Assessment");

        if (holdingClient is not null)
        {
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Running,
                Remaining(deadline), "the held Assessment did not reach Running");
            Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
            Assert.True(walkthrough.Find<Button>("Cancel the running Assessment").IsEffectivelyEnabled);
            holdingClient.ReleaseAssess();
        }
    }
}
