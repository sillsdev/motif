using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App;

namespace SIL.Motif.App.Views;

/// <summary>The Settings flyout attached to the window's gear button.</summary>
public sealed partial class SettingsPopupView : UserControl
{
    /// <summary>Raised when a person asks to close the Settings flyout.</summary>
    public event EventHandler? CloseRequested;
    /// <summary>Raised when a person opens the page-specific Help flyout.</summary>
    public event EventHandler? OpenHelpRequested;
    /// <summary>Raised when a person opens the problem-report preview.</summary>
    public event EventHandler? ReportProblemRequested;
    /// <summary>Raised when a person opens the public documentation site.</summary>
    public event EventHandler? OpenSiteRequested;
    /// <summary>Raised when a person opens the folder Motif uses for its own data.</summary>
    public event EventHandler? OpenDataFolderRequested;
    /// <summary>Raised when a person copies the product and parser version lines.</summary>
    public event EventHandler? CopyVersionsRequested;
    /// <summary>Raised when a person opens project setup to choose a Selection.</summary>
    public event EventHandler? ConfigureRequested;

    /// <summary>Creates the Settings flyout and installs its keyboard handling.</summary>
    public SettingsPopupView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(InputElement.KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
    }

    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    /// <summary>Moves focus to the currently selected group when the flyout opens.</summary>
    public void FocusSelectedGroup() => this.FindControl<ListBox>("SettingsGroups")?.Focus(NavigationMethod.Tab);

    /// <summary>Moves focus to shortcut search after the shared shortcut registry opens this group.</summary>
    public void FocusShortcutSearch() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        this.FindControl<TextBox>("ShortcutSearchBox")?.Focus(NavigationMethod.Tab));

    /// <summary>Scrolls the Writing systems list to the requested project writing system.</summary>
    public void FocusWritingSystem(string writingSystemId) => Dispatcher.UIThread.Post(() =>
    {
        var row = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(control =>
            control.DataContext is WritingSystemSettingsRow writingSystem && writingSystem.Id == writingSystemId);
        row?.BringIntoView();
    });

    /// <summary>Fits the popup's unscaled layout inside the available window area.</summary>
    /// <param name="maxLogicalWidth">The popup's remaining width before applying that scale.</param>
    /// <param name="maxLogicalHeight">The popup's remaining height before applying that scale.</param>
    public void FitToAvailableArea(double maxLogicalWidth, double maxLogicalHeight)
    {
        var surface = this.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(control => control.Name == "SettingsSurface");
        if (surface is null) return;
        var maxSurfaceWidth = Math.Min(double.IsFinite(surface.Width) ? surface.Width : maxLogicalWidth,
            maxLogicalWidth);
        var maxSurfaceHeight = Math.Min(double.IsFinite(surface.Height) ? surface.Height : maxLogicalHeight,
            maxLogicalHeight);
        if (double.IsFinite(maxSurfaceWidth)) surface.MaxWidth = Math.Max(0, maxSurfaceWidth);
        if (double.IsFinite(maxSurfaceHeight)) surface.MaxHeight = Math.Max(0, maxSurfaceHeight);
    }

    private void OnCloseRequested(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenHelpRequested(object? sender, RoutedEventArgs e) => OpenHelpRequested?.Invoke(this, EventArgs.Empty);
    private void OnReportProblemRequested(object? sender, RoutedEventArgs e) => ReportProblemRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenSiteRequested(object? sender, RoutedEventArgs e) => OpenSiteRequested?.Invoke(this, EventArgs.Empty);
    private void OnOpenDataFolderRequested(object? sender, RoutedEventArgs e) => OpenDataFolderRequested?.Invoke(this, EventArgs.Empty);
    private void OnCopyVersionsRequested(object? sender, RoutedEventArgs e) => CopyVersionsRequested?.Invoke(this, EventArgs.Empty);
    private void OnConfigureRequested(object? sender, RoutedEventArgs e) => ConfigureRequested?.Invoke(this, EventArgs.Empty);
    private async void OnUseEstimateClick(object? sender, RoutedEventArgs e)
    {
        if (Settings is null) return;
        Settings.UseMotifEstimate();
        await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    private async void OnNoStepLimitClick(object? sender, RoutedEventArgs e)
    {
        if (Settings is not null) await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    private async void OnParsingLimitLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Settings is not null) await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    [KeyboardShortcutHandler("Settings:CommitParsingLimits", "Settings:CancelParsingLimits")]
    private async void OnParsingLimitKeyDown(object? sender, KeyEventArgs e)
    {
        if (Settings is null) return;
        if (KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
                targetBehaviors: [KeyboardShortcutBehavior.CommitParsingLimits]) is not null)
        {
            e.Handled = true;
            await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
        }
        else if (KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
                     targetBehaviors: [KeyboardShortcutBehavior.CancelParsingLimits]) is not null &&
                 Settings.HasUncommittedParsingLimitChanges)
        {
            Settings.CancelParsingLimitEdit();
            e.Handled = true;
        }
    }

    [KeyboardShortcutHandler("Settings:Back", "Settings:FocusCloseButton", "Settings:FocusSettingsGroups")]
    private void OnSettingsKeyDown(object? sender, KeyEventArgs e)
    {
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var focusClose = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.FocusCloseButton]);
        var focusGroups = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.FocusSettingsGroups]);
        if (focusClose is not null && ReferenceEquals(focused, this.FindControl<ListBox>("SettingsGroups")))
        {
            e.Handled = this.FindControl<Button>("SettingsClose")?.Focus(NavigationMethod.Tab) == true;
            return;
        }
        if (focusGroups is not null && ReferenceEquals(focused, this.FindControl<Button>("SettingsClose")))
        {
            e.Handled = this.FindControl<ListBox>("SettingsGroups")?.Focus(NavigationMethod.Tab) == true;
            return;
        }
        var back = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.Back]);
        if (back is null || Settings is null) return;
        if (ReferenceEquals(focused, this.FindControl<TextBox>("ShortcutSearchBox")) && Settings.ShortcutSearchHasText)
        {
            Settings.ShortcutSearch = string.Empty;
            e.Handled = true;
            return;
        }
        if ((focused is NumericUpDown || focused?.FindAncestorOfType<NumericUpDown>() is not null) &&
            Settings.HasUncommittedParsingLimitChanges)
        {
            Settings.CancelParsingLimitEdit();
            e.Handled = true;
            return;
        }
        e.Handled = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [KeyboardShortcutHandler("Settings:PreviousThemeChoice", "Settings:NextThemeChoice")]
    private void OnThemeSegmentKeyDown(object? sender, KeyEventArgs e)
    {
        if (Settings is null) return;
        var previous = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.PreviousThemeChoice]);
        var next = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.NextThemeChoice]);
        if (previous is null && next is null) return;
        Settings.MoveThemeChoice(previous is not null ? -1 : 1);
        var selectedControlName = Settings.ThemeChoice switch
        {
            WindowThemeChoice.Light => "ThemeLightButton",
            WindowThemeChoice.Dark => "ThemeDarkButton",
            _ => "ThemeSystemButton",
        };
        e.Handled = this.FindControl<Button>(selectedControlName)?.Focus(NavigationMethod.Directional) == true;
    }
}
