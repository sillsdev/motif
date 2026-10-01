using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Sdk;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class WalkthroughSteps
{
    internal static void ChooseProjectAndCaptureBaseline(WalkthroughWindow walkthrough, long deadline)
    {
        walkthrough.Show();

        Assert.Empty(walkthrough.Workspace.Project.KnownProjects);
        walkthrough.OpenProjectMenu();
        Assert.True(walkthrough.FindProjectMenuEntry<Button>("Select a new project").IsEffectivelyEnabled);
        walkthrough.ChooseNewProject();
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Baseline.CapturedTimeText == "No Baseline captured yet" &&
                walkthrough.Workspace.Selection.TextsEmptyMessage == "Capture a Baseline to choose Texts.",
            Remaining(deadline), "choosing the project did not load its initial window state");
        Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal);
        Assert.Null(walkthrough.Workspace.Selection.ShownRefusal);
        Assert.True(walkthrough.Find<Button>("Refresh the project").IsEffectivelyEnabled);
        Assert.False(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);
        Assert.False(walkthrough.Find<Button>("Write the AI Handoff folder").IsEffectivelyEnabled);
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
        // Freshness is said once, in the top bar; the Overview repeats none of it.
        Assert.DoesNotContain(walkthrough.Window.GetLogicalDescendants().OfType<TextBlock>(), text =>
            text.IsEffectivelyVisible && text.Text is { } shown &&
            (shown.Contains("last FieldWorks save", StringComparison.Ordinal) ||
             shown.StartsWith("Baseline ", StringComparison.Ordinal) && shown.Contains("saved", StringComparison.Ordinal)));
        Assert.Equal("FieldWorks does not currently hold this project.",
            walkthrough.Workspace.Baseline.HeldStatusText);
        Assert.Null(walkthrough.Workspace.Baseline.ShownRefusal);
        Assert.Null(walkthrough.Workspace.Selection.ShownRefusal);
        Assert.Equal(SeededProject.TextTitle, Assert.Single(walkthrough.Workspace.Selection.Texts).Title);
    }

    internal static TimeSpan Remaining(long deadline)
    {
        var ticks = deadline - Stopwatch.GetTimestamp();
        return ticks > 0 ? TimeSpan.FromSeconds((double)ticks / Stopwatch.Frequency) : TimeSpan.Zero;
    }

    internal static void RunAssessmentOverPastedWords(WalkthroughWindow walkthrough, long deadline)
    {
        StartAssessmentOverPastedWords(walkthrough, deadline);
        // The pages go on loading what the run published, and a click aimed meanwhile can miss as they fill in.
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
            Remaining(deadline), "the Assessment did not complete and reach every page");
    }

    internal static void EnsureAssessmentForAnalyze(WalkthroughWindow walkthrough, TimeSpan timeout)
    {
        if (!walkthrough.Workspace.Context.NeedsAssessment) return;
        var setup = walkthrough.Workspace.Context.Setup!;
        if (setup.CanRunDefaultSelection)
        {
            if (setup.IsOpen) walkthrough.SkipSetup();
            walkthrough.ShowPage(WorkspacePage.Texts);
            SetupWalkthroughActions.ClickParseAllWordsFromTexts(walkthrough);
        }
        else
        {
            if (!setup.IsOpen)
            {
                walkthrough.ConfigureFromProjectMenu();
                walkthrough.WaitUntil(
                    () => setup.IsOpen && setup.ConfigurationLoadTask?.IsCompleted != false,
                    timeout, "Configure did not open the first-run Selection");
            }

            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: texts");
            if (walkthrough.Workspace.Selection.Texts.FirstOrDefault() is { } text)
                SetupWalkthroughActions.SetSetupTextChecked(walkthrough, text.Title, true);
            else
                walkthrough.Type("Words to add", "motifa");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: limits");
            SetupWalkthroughActions.ClickSetupButton(walkthrough, "Next: first run");
            walkthrough.Click("Start first run");
        }

        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted && !setup.IsOpen,
            timeout, "the Default Selection did not finish parsing before opening Analyze texts");
    }

    internal static void StartSlowAssessment(
        WalkthroughWindow walkthrough, long deadline, IReadOnlyList<string> words)
    {
        walkthrough.TypePastedWords(string.Join(Environment.NewLine, words));
        Assert.True(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);

        walkthrough.Click("Parse all words in the Selection");
        Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
        Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
        walkthrough.WaitUntil(() =>
        {
            if (walkthrough.Workspace.Assess.State is RunState.Completed or RunState.Cancelled or RunState.Refused)
                throw new XunitException("The slow Assessment finished before its cancellable state appeared.");

            return walkthrough.Workspace.Assess.State == RunState.Running &&
                walkthrough.Find<Button>("Cancel parsing").IsEffectivelyEnabled;
        }, Remaining(deadline), "the slow Assessment did not reach its cancellable Running state");
    }

    internal static void StartAssessmentOverPastedWords(
        WalkthroughWindow walkthrough, long deadline, HoldingStartGate? holdingGate = null)
    {
        walkthrough.TypePastedWords("motifa\nmotifb\nmofita");
        Assert.True(walkthrough.Find<Button>("Parse all words in the Selection").IsEffectivelyEnabled);

        walkthrough.Click("Parse all words in the Selection");

        if (holdingGate is not null)
        {
            walkthrough.WaitUntil(
                () => walkthrough.Workspace.Assess.State == RunState.Running,
                Remaining(deadline), "the held Assessment did not reach Running");
            Assert.False(walkthrough.Find<Button>("Project menu").IsEffectivelyEnabled);
            Assert.False(walkthrough.Named<ContentControl>("SelectionHost").IsEffectivelyEnabled);
            Assert.True(walkthrough.Find<Button>("Cancel parsing").IsEffectivelyEnabled);
            holdingGate.ReleaseAssess();
        }
    }
}
