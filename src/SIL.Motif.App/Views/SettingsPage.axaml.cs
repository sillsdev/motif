using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>The scrollable Settings list with section navigation supplied to the workspace.</summary>
public sealed partial class SettingsPage : UserControl, IWorkspacePaneProvider
{
    private bool? _aboutRowsAreCompact;
    private bool _anchorSelectedGroup;
    private readonly SettingsPageModel? _pageModel;

    /// <summary>The title of Settings’ section navigation.</summary>
    public string SidePanelTitle => "Settings";

    /// <summary>The section list attached beside the Settings page.</summary>
    public Control SidePanel => Navigation;

    /// <summary>Raised when a person asks to close the Settings page.</summary>
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

    /// <summary>Creates the Settings page and installs its keyboard handling.</summary>
    public SettingsPage()
    {
        AvaloniaXamlLoader.Load(this);
        DataContextChanged += OnPageDataContextChanged;
        AttachedToVisualTree += (_, _) =>
        {
            if (_pageModel is not null)
            {
                _pageModel.PropertyChanged += OnSettingsModelChanged;
                DataContext = _pageModel.Settings;
            }
            ObserveSettings();
            ScrollToSelectedGroup();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            if (_pageModel is not null) _pageModel.PropertyChanged -= OnSettingsModelChanged;
            if (_observedSettings is not null) _observedSettings.PropertyChanged -= OnSettingsChanged;
            _observedSettings = null;
        };
        AddHandler(InputElement.PointerPressedEvent, (_, _) => _anchorSelectedGroup = false,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => _anchorSelectedGroup = false,
            RoutingStrategies.Tunnel, handledEventsToo: true);
        Navigation.AddHandler(InputElement.KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
        AddHandler(InputElement.KeyDownEvent, OnSettingsKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>Uses the window Settings model assigned through this page's registration model.</summary>
    public SettingsPage(SettingsPageModel model) : this()
    {
        ArgumentNullException.ThrowIfNull(model);
        _pageModel = model;
        DataContext = model.Settings;
    }

    /// <summary>The section navigator supplied to the workspace side panel.</summary>
    public SettingsNavigation Navigation { get; } = new();

    private SettingsViewModel? _observedSettings;
    private SettingsViewModel? Settings => DataContext as SettingsViewModel;

    private void OnSettingsModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsPageModel.Settings) && sender is SettingsPageModel model)
            DataContext = model.Settings;
    }

    private void OnPageDataContextChanged(object? sender, EventArgs e)
    {
        ObserveSettings();
        ScrollToSelectedGroup();
    }

    private void ObserveSettings()
    {
        if (_observedSettings is not null) _observedSettings.PropertyChanged -= OnSettingsChanged;
        _observedSettings = Settings;
        Navigation.DataContext = Settings;
        if (_observedSettings is not null) _observedSettings.PropertyChanged += OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.SelectedGroup)) ScrollToSelectedGroup();
    }

    /// <summary>Scrolls to the selected section without hiding the other Settings sections.</summary>
    public void ScrollToSelectedGroup()
    {
        _anchorSelectedGroup = true;
        Dispatcher.UIThread.Post(() =>
        {
            var section = Settings?.SelectedGroup switch
            {
                SettingsGroup.Display => "SettingsDisplaySection",
                SettingsGroup.KeyboardShortcuts => "SettingsKeyboardShortcutsSection",
                SettingsGroup.Parsing => "SettingsParsingSection",
                SettingsGroup.ProposalChecks => "SettingsProposalChecksSection",
                SettingsGroup.WritingSystems => "SettingsWritingSystemsSection",
                SettingsGroup.Texts => "SettingsTextsSection",
                SettingsGroup.HelpAndReports => "SettingsHelpAndReportsSection",
                SettingsGroup.About => "SettingsAboutSection",
                _ => "SettingsDisplaySection",
            };
            if (this.FindControl<ScrollViewer>("SettingsListScroll") is { Content: Control content } scroll &&
                this.FindControl<Control>(section) is { } target &&
                target.TranslatePoint(default, content) is { } origin)
                scroll.Offset = new Vector(0, origin.Y + content.Margin.Top);
        });
    }

    private void OnSettingsContentSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (_anchorSelectedGroup) ScrollToSelectedGroup();
        var breakpoint = (double)Application.Current!.FindResource("Component.Settings.AboutStackBreakpoint")!;
        var compact = e.NewSize.Width < breakpoint;
        if (_aboutRowsAreCompact == compact) return;
        foreach (var row in this.GetVisualDescendants().OfType<Grid>()
                     .Where(grid => grid.Classes.Contains("settingsRow") && grid.Children.Count == 2))
        {
            row.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,Auto");
            row.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            Grid.SetColumn(row.Children[1], compact ? 0 : 1);
            Grid.SetRow(row.Children[1], compact ? 1 : 0);
        }
        _aboutRowsAreCompact = compact;
        foreach (var row in this.GetVisualDescendants().OfType<Grid>()
                     .Where(grid => grid.Classes.Contains("settingsAboutRow")))
            SetAboutRowLayout(row, compact);

        if (this.FindControl<Grid>("SettingsAboutDataRow") is { } dataRow)
            SetAboutDataRowLayout(dataRow, compact);
    }

    private void OnWritingSummarySizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (sender is not Grid row) return;
        var breakpoint = (double)Application.Current!.FindResource("Component.Settings.AboutStackBreakpoint")!;
        var compact = e.NewSize.Width < breakpoint;
        if (row.ColumnDefinitions.Count == (compact ? 1 : 3)) return;
        row.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "*,*,*");
        row.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto,Auto" : "Auto");
        for (var index = 0; index < row.Children.Count; index++)
        {
            Grid.SetColumn(row.Children[index], compact ? 0 : index);
            Grid.SetRow(row.Children[index], compact ? index : 0);
        }
    }

    private static void SetAboutRowLayout(Grid row, bool compact)
    {
        var label = row.Children.OfType<Control>()
            .Single(control => control.Classes.Contains("settingsAboutLabel"));
        var value = row.Children.OfType<Control>().Single(control => !ReferenceEquals(control, label));
        label.Classes.Set("compact", compact);

        row.ColumnDefinitions.Clear();
        row.ColumnDefinitions.Add(new ColumnDefinition(compact ? GridLength.Star : GridLength.Auto));
        if (!compact) row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        row.RowDefinitions.Clear();
        row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (compact) row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetColumn(label, 0);
        Grid.SetRow(label, 0);
        Grid.SetColumn(value, compact ? 0 : 1);
        Grid.SetRow(value, compact ? 1 : 0);
    }

    private static void SetAboutDataRowLayout(Grid row, bool compact)
    {
        var value = row.Children.OfType<TextBlock>().Single(control => control.Classes.Contains("settingsAboutValue"));
        var button = row.Children.OfType<Button>().Single();
        row.ColumnDefinitions.Clear();
        row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (!compact) row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        row.RowDefinitions.Clear();
        row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        if (compact) row.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetColumn(value, 0);
        Grid.SetRow(value, 0);
        Grid.SetColumn(button, compact ? 0 : 1);
        Grid.SetRow(button, compact ? 1 : 0);
        button.HorizontalAlignment = compact
            ? Avalonia.Layout.HorizontalAlignment.Left
            : Avalonia.Layout.HorizontalAlignment.Right;
    }

    /// <summary>Moves focus to the currently selected group when the page opens.</summary>
    public void FocusSelectedGroup()
    {
        if (Navigation.FindControl<ListBox>("SettingsGroups")?.Focus(NavigationMethod.Tab) != true)
            this.FindControl<Button>("SettingsClose")?.Focus(NavigationMethod.Tab);
    }

    /// <summary>Moves focus to shortcut search after the shared shortcut registry opens this group.</summary>
    public void FocusShortcutSearch() => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        if (this.FindControl<TextBox>("ShortcutSearchBox") is { } search)
        {
            search.BringIntoView();
            search.Focus(NavigationMethod.Tab);
        }
    });

    /// <summary>Scrolls the Writing systems list to the requested project writing system.</summary>
    public void FocusWritingSystem(string writingSystemId) => Dispatcher.UIThread.Post(() =>
    {
        var row = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(control =>
            control.DataContext is WritingSystemSettingsRow writingSystem && writingSystem.Id == writingSystemId);
        if (row is not null)
        {
            row.FindAncestorOfType<Expander>()?.SetCurrentValue(Expander.IsExpandedProperty, true);
            row.BringIntoView();
        }
    });

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
        if (e.Source is Visual origin && origin.GetSelfAndVisualAncestors().Contains(this))
            _anchorSelectedGroup = false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Control;
        var focusClose = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.FocusCloseButton]);
        var focusGroups = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Settings, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.FocusSettingsGroups]);
        if (focusClose is not null && ReferenceEquals(focused, Navigation.FindControl<ListBox>("SettingsGroups")))
        {
            e.Handled = this.FindControl<Button>("SettingsClose")?.Focus(NavigationMethod.Tab) == true;
            return;
        }
        if (focusGroups is not null && ReferenceEquals(focused, this.FindControl<Button>("SettingsClose")))
        {
            e.Handled = Navigation.FindControl<ListBox>("SettingsGroups")?.Focus(NavigationMethod.Tab) == true;
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
