using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App.Walkthrough;

internal static class SetupWalkthroughActions
{
    internal static void ClickSetupButton(WalkthroughWindow walkthrough, string accessibleName)
    {
        var button = walkthrough.Window.GetLogicalDescendants().OfType<Button>().Single(candidate =>
            string.Equals(Avalonia.Automation.AutomationProperties.GetName(candidate), accessibleName,
                StringComparison.Ordinal) || Equals(candidate.Content, accessibleName));
        HeadlessClick.Click(walkthrough.Window, button, accessibleName);
    }

    internal static void ClickParseAllWordsFromTexts(WalkthroughWindow walkthrough)
    {
        var texts = Assert.Single(walkthrough.Window.GetLogicalDescendants().OfType<TextsPage>());
        var button = Assert.Single(texts.GetLogicalDescendants().OfType<Button>(), candidate =>
            Avalonia.Automation.AutomationProperties.GetName(candidate) == "Parse all words");
        HeadlessClick.Click(walkthrough.Window, button, "Parse all words");
    }

    internal static void SelectProject(WalkthroughWindow walkthrough, string projectPath)
    {
        if (!walkthrough.Window.IsVisible) walkthrough.Show();
        walkthrough.ProjectPath = projectPath;
        walkthrough.ClickProjectMenuEntry("Select a new project");
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Context.ProjectPath == projectPath &&
                walkthrough.Workspace.Context.Setup?.ProjectPath == projectPath &&
                (walkthrough.Workspace.Selection.Texts.Count > 0 ||
                    walkthrough.Workspace.Selection.TextsEmptyMessage is not null),
            TimeSpan.FromSeconds(60), "selecting the project did not load its setup state");
    }

    internal static void CaptureBaselineAndWaitForSetup(
        WalkthroughWindow walkthrough, int textCount, TimeSpan timeout)
    {
        walkthrough.Click("Refresh the project");
        var setup = walkthrough.Workspace.Context.Setup!;
        var baseline = walkthrough.Workspace.Baseline;
        walkthrough.WaitUntil(
            () => baseline.ShownRefusal is not null ||
                setup.IsOpen && baseline.HasBaseline && walkthrough.Workspace.Selection.Texts.Count == textCount,
            timeout, "Refresh did not open setup with the project's Texts");
        Assert.True(baseline.ShownRefusal is null,
            $"Refresh was refused: {baseline.ShownRefusal?.Sentence} {baseline.ShownRefusal?.Details}");
        walkthrough.WaitUntil(
            () => !walkthrough.Workspace.RefreshCommand.IsRunning,
            timeout, "Refresh did not finish loading the captured Baseline");
    }

    internal static void SetSetupTextChecked(
        WalkthroughWindow walkthrough, string title, bool isChecked)
    {
        var row = walkthrough.Window.GetLogicalDescendants().OfType<Border>()
            .Where(border => border.Classes.Contains("setupTextRow"))
            .Single(border => border.GetLogicalDescendants().OfType<TextBlock>()
                .Any(text => text.Text == title));
        var checkBox = row.GetLogicalDescendants().OfType<CheckBox>().Single();
        if (checkBox.IsChecked != isChecked)
            HeadlessClick.Click(walkthrough.Window, checkBox, title);
        Assert.Equal(isChecked, checkBox.IsChecked);
    }

    internal static void TypeSetupLimit(WalkthroughWindow walkthrough, string accessibleName, string value)
    {
        var number = walkthrough.Find<NumericUpDown>(accessibleName);
        number.ApplyTemplate();
        var editor = number.GetVisualDescendants().OfType<TextBox>().Single();
        HeadlessClick.Click(walkthrough.Window, editor, accessibleName);
        Assert.True(editor.IsFocused, $"'{accessibleName}' did not receive focus from the click.");
        walkthrough.Window.KeyPress(Key.A, RawInputModifiers.Control, PhysicalKey.None, null);
        walkthrough.Window.KeyTextInput(value);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void OpenConfigure(WalkthroughWindow walkthrough)
    {
        walkthrough.ConfigureFromProjectMenu();
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Context.Setup?.IsOpen == true,
            TimeSpan.FromSeconds(30), "Configure did not open setup");
    }

    internal static void FinishFirstRun(
        WalkthroughWindow walkthrough, string selectedText, string stepLimit,
        TimeSpan timeout, string? addedWords = null)
    {
        var setup = walkthrough.Workspace.Context.Setup!;
        ClickSetupButton(walkthrough, "Next: texts");
        Assert.Equal(1, setup.Step);
        SetSetupTextChecked(walkthrough, selectedText, true);
        if (addedWords is not null) walkthrough.Type("Words to add", addedWords);
        ClickSetupButton(walkthrough, "Next: limits");
        TypeSetupLimit(walkthrough, "Parser step limit per word", stepLimit);
        ClickSetupButton(walkthrough, "Next: first run");
        Assert.Equal(3, setup.Step);
        var heartbeat = Path.Combine(walkthrough.ManagedRoot, "first-run-held-heartbeat");
        walkthrough.SetFakeParserBehavior(new
        {
            subcommands = new Dictionary<string, object>
            {
                ["batch"] = new { heartbeatPath = heartbeat },
            },
        });
        SIL.Motif.Contract.Responses.AssessmentStage? stageWhenSetupClosed = null;
        setup.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SetupViewModel.IsOpen) && !setup.IsOpen)
                stageWhenSetupClosed = walkthrough.Workspace.Assess.Progress?.Stage;
        };
        walkthrough.Click("Start first run");
        walkthrough.WaitUntil(
            () => File.Exists(heartbeat) || walkthrough.Workspace.Assess.State is
                RunState.Completed or RunState.Cancelled or RunState.Refused,
            timeout, "Finish did not reach the held parser");
        Assert.True(File.Exists(heartbeat), "Finish completed without holding the batch parser.");
        Assert.False(setup.IsOpen);
        Assert.Equal(SIL.Motif.Contract.Responses.AssessmentStage.Capturing, stageWhenSetupClosed);
        walkthrough.Click("Cancel the running Assessment");
        walkthrough.WaitUntil(() => walkthrough.Workspace.Assess.State == RunState.Cancelled,
            timeout, "the held first run did not cancel");
        walkthrough.SetFakeParserBehavior(new
        {
            subcommands = new Dictionary<string, object>
            {
                ["batch"] = new { words = new[] { new { word = selectedText, outcome = "complete" } } },
            },
        });
        ClickParseAllWordsFromTexts(walkthrough);
        walkthrough.WaitUntil(
            () => walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
            timeout, "the first run did not complete after the held parser was released");
    }
}
