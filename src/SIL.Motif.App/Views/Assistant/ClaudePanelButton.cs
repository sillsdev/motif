using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Assistant;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>Toggles the shell's assistant slot without changing the page or its selection.</summary>
public sealed class ClaudePanelButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    private WorkspaceLayoutHost? _layout;
    private ClaudePanel? _panel;
    private Control? _previousFocus;

    public ClaudePanelButton()
    {
        Content = MotifAppStrings.BoardClaude;
        AutomationProperties.SetAutomationId(this, AutomationIds.ClaudeToggle);
        AutomationProperties.SetName(this, MotifAppStrings.BoardClaude);
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (DataContext is not WorkspaceShellViewModel workspace || TopLevel.GetTopLevel(this) is not { } window) return;
        _layout ??= window.GetVisualDescendants().OfType<WorkspaceLayoutHost>().FirstOrDefault();
        if (_layout is null) return;
        if (_layout.ClaudePanel is not null) { ClosePanel(); return; }
        _previousFocus = window.FocusManager?.GetFocusedElement() as Control;
        var model = workspace.PageModel<SettingsPageModel>().Claude;
        _panel = new ClaudePanel(model);
        _panel.CloseRequested += (_, _) => ClosePanel();
        _panel.SetupRequested += (_, _) =>
        {
            ClosePanel();
            window.GetVisualDescendants().OfType<MotifWorkspaceView>().Single().OpenSettings();
            workspace.PageModel<SettingsPageModel>().Settings?.OpenGroup(SettingsGroup.AssistantConnections);
        };
        _layout.ClaudePanel = _panel;
        _layout.SizeChanged += OnLayoutSizeChanged;
        UpdatePlacement();
        _panel.FindControl<Button>("CloseButton")!.Focus();
        model.RefreshCommand.Execute(null);
    }

    private void OnLayoutSizeChanged(object? sender, SizeChangedEventArgs e) => UpdatePlacement();

    private void UpdatePlacement()
    {
        if (_layout is null || _panel is null) return;
        var slot = _layout.FindControl<ContentControl>("ClaudePanelHost")!;
        var narrow = _layout.Bounds.Width < (double)this.FindResource("Component.ClaudePanel.Breakpoint")!;
        Grid.SetColumn(slot, narrow ? 1 : 4);
        Grid.SetColumnSpan(slot, narrow ? 4 : 1);
        if (narrow) _layout.ShowWorkingPage();
    }

    private void ClosePanel()
    {
        if (_layout is null) return;
        _layout.SizeChanged -= OnLayoutSizeChanged;
        _layout.ClaudePanel = null;
        _panel = null;
        if (_previousFocus?.IsEffectivelyVisible != true || !_previousFocus.Focus()) Focus();
        _previousFocus = null;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        ClosePanel();
        _layout = null;
        base.OnDetachedFromVisualTree(e);
    }
}
