using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
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
        walkthrough.WaitUntil(
            () => setup.IsOpen && walkthrough.Workspace.Baseline.HasBaseline &&
                walkthrough.Workspace.Selection.Texts.Count == textCount,
            timeout, "Refresh did not open setup with the project's Texts");
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
        TimeSpan timeout)
    {
        var setup = walkthrough.Workspace.Context.Setup!;
        ClickSetupButton(walkthrough, "Next: texts");
        Assert.Equal(1, setup.Step);
        SetSetupTextChecked(walkthrough, selectedText, true);
        ClickSetupButton(walkthrough, "Next: limits");
        TypeSetupLimit(walkthrough, "Parser step limit per word", stepLimit);
        ClickSetupButton(walkthrough, "Next: first run");
        Assert.Equal(3, setup.Step);
        walkthrough.Click("Start first run");
        walkthrough.WaitUntil(
            () => !setup.IsOpen && walkthrough.Workspace.Assess.State == RunState.Completed &&
                walkthrough.Workspace.Context.EvidencePublication.IsCompleted,
            timeout, "Finish did not save the Selection and complete the first run");
    }
}
