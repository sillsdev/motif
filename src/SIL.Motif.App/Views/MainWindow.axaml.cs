using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// The single-window shell — a top bar, a sidebar of pages, and the page on screen — as a minimal frame
/// until <see cref="Compose"/> supplies its workspace and builds the panels, so a caller can construct and
/// inspect the bare shell (<c>AppSmokeTests</c>) without standing up any command client or desktop adapter.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly IUriLauncher _uriLauncher;
    private readonly ProblemReportWindowServices _problemReportServices;
    private readonly IUserPreferencesStore _preferences;
    private readonly ApplicationFacts _applicationFacts;
    private ProblemReportPreviewWindow? _currentProblemReportPreview;
    private string? _helpAutomationId;
    private readonly DispatcherTimer _keyboardStatusTimer;
    private Control? _inspectedFrom;
    private SettingsViewModel? _settings;
    private HelpPopupView? HelpPopup =>
        this.FindControl<Button>("HelpButton")?.Flyout is Flyout flyout ? flyout.Content as HelpPopupView : null;
    private SettingsPopupView? SettingsPopup =>
        this.FindControl<Button>("SettingsButton")?.Flyout is Flyout flyout ? flyout.Content as SettingsPopupView : null;

    /// <summary>A window at the XAML's own size that neither reads nor writes the remembered bounds, as tests need.</summary>
    public MainWindow() : this(rememberBounds: false)
    {
    }

    /// <summary>
    /// A window that, when <paramref name="rememberBounds"/> is true, reopens at the size and place the person
    /// last left it and saves them again on close.
    /// </summary>
    public MainWindow(bool rememberBounds)
        : this(rememberBounds, uriLauncher: null)
    {
    }

    /// <summary>Builds the window with an optional launcher for its online Help links.</summary>
    /// <param name="rememberBounds">Whether to restore and save the window's size and place.</param>
    /// <param name="uriLauncher">The adapter that opens online links, or the window's launcher when omitted.</param>
    public MainWindow(bool rememberBounds, IUriLauncher? uriLauncher)
        : this(rememberBounds, uriLauncher, preferencesStore: null)
    {
    }

    /// <summary>Builds the window with optional adapters and a shared preference store.</summary>
    /// <param name="rememberBounds">Whether to restore and save the window's size and place.</param>
    /// <param name="uriLauncher">The adapter that opens online links, or the window's launcher when omitted.</param>
    /// <param name="preferencesStore">The shared preferences store, or a process-local store when omitted.</param>
    public MainWindow(bool rememberBounds, IUriLauncher? uriLauncher, IUserPreferencesStore? preferencesStore,
        ApplicationFacts? applicationFacts = null)
    {
        _preferences = preferencesStore ?? new MemoryUserPreferencesStore();
        _applicationFacts = applicationFacts ?? ApplicationFacts.Current;
        AvaloniaXamlLoader.Load(this);
        _keyboardStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _keyboardStatusTimer.Tick += (_, _) =>
        {
            _keyboardStatusTimer.Stop();
            ShowKeyboardStatus(null);
        };
        _uriLauncher = uriLauncher ?? new AvaloniaLauncher(this);
        _problemReportServices = ProblemReportWindowServices.ForWindow(this, _uriLauncher);
        if (HelpPopup is { } helpView)
            helpView.DataContext = new HelpPopupViewModel(_uriLauncher);
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (this.FindControl<StackPanel>("TopBarActions") is { } actions)
            foreach (var action in actions.Children.OfType<Button>())
                action.AddHandler(ToolTip.ToolTipOpeningEvent, (_, _) => PlaceBesideTopBarActions(actions, action));
        if (!rememberBounds) return;
        RestoreBounds();
        Opened += (_, _) => ConstrainRestoredBounds();
        Closing += (_, _) => SaveBounds();
    }

    private async void OnParseReportProblemClick(object? sender, RoutedEventArgs e) =>
        await ShowProblemReportAsync(ProblemReport.ForStalledParse(_applicationFacts)).ConfigureAwait(true);

    private async void OnMachineStoreReportProblemClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceShellViewModel { Project.MachineStoreProblemReport: { } report })
            await ShowProblemReportAsync(report).ConfigureAwait(true);
    }

    /// <summary>Shows the reviewed report before a person copies it or opens an issue form.</summary>
    /// <param name="report">The report whose current text appears in the preview.</param>
    public Task ShowProblemReportAsync(ProblemReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return ShowProblemReportPreviewAsync(report);
    }

    /// <summary>The report preview currently owned by this window, if one is open.</summary>
    internal ProblemReportPreviewWindow? CurrentProblemReportPreview => _currentProblemReportPreview;
    internal ApplicationFacts ApplicationFacts => _applicationFacts;

    private async Task ShowProblemReportPreviewAsync(ProblemReport report)
    {
        var preview = new ProblemReportPreviewWindow(report, _problemReportServices);
        _currentProblemReportPreview = preview;
        try
        {
            await preview.ShowDialog(this).ConfigureAwait(true);
        }
        finally
        {
            _currentProblemReportPreview = null;
        }
    }

    // The tip opens left of the whole group, so it hides neither the action's neighbours nor the notice below.
    private static void PlaceBesideTopBarActions(StackPanel actions, Button action)
    {
        if (actions.TranslatePoint(default, action) is { } groupStart)
            ToolTip.SetHorizontalOffset(action, groupStart.X - actions.Spacing);
    }

    /// <summary>Builds each page's view from its model with <see cref="PageRegistry"/>, and binds the window to <paramref name="workspace"/>.</summary>
    public void Compose(WorkspaceShellViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;
        WritingSystemText.SetResolver(this, workspace.Context.WritingSystemTextStyles);
        ConfigureSettings(workspace);

        var host = this.FindControl<Panel>("PageHost")
            ?? throw new InvalidOperationException("MainWindow.axaml has no element named 'PageHost'.");
        workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuide = OpenPanGlossGuide;
        foreach (var entry in workspace.Pages)
        {
            var view = entry.Model is TextsPageModel texts
                ? new TextsPage(texts, _settings)
                : PageRegistry.For(entry.Page).CreateView(entry.Model);
            view.Name = $"{entry.Page}Page";
            view.Bind(IsVisibleProperty, new Binding(nameof(PageViewModel.IsCurrent)) { Source = entry });
            host.Children.Add(view);
        }

        AddHandler(InspectLink.RequestedEvent, (_, e) => OnInspectRequested(workspace, e));
        workspace.Inspector.Closed += (_, _) => ReturnFocusFromInspector();

        workspace.UpdateWindowWidth(Width / (_settings?.ZoomScale ?? 1));
        SizeChanged += (_, e) =>
        {
            workspace.UpdateWindowWidth(e.NewSize.Width / (_settings?.ZoomScale ?? 1));
            UpdateSettingsFlyoutZoom();
            UpdateSetupDialogZoom();
            RepositionSettingsFlyout();
        };
        if (this.FindControl<Button>("ProjectMenuButton")?.Flyout is Flyout projectMenu)
            projectMenu.Opened += (_, _) => _ = workspace.RefreshKnownProjectsAsync();
        // Coming back from FieldWorks is when a save it made is news; this reads, it never reruns.
        Activated += (_, _) => _ = workspace.CheckFreshnessAsync();
        workspace.RecentProjects.CollectionChanged += (_, _) => RebuildRecentProjects(workspace);
        RebuildRecentProjects(workspace);
        WindowZoomPolicy.EnablePopupTransforms(this);
    }

    // A name inside the inspector adds a crumb; one on a page opens the inspector afresh beside that page.
    private void OnInspectRequested(WorkspaceShellViewModel workspace, InspectRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Origin.FindAncestorOfType<Inspector>() is not null)
        {
            workspace.Inspector.Push(e.Subject);
            return;
        }
        _inspectedFrom = e.Origin;
        workspace.Context.OpenInspector(e.Subject, InspectLink.GetFrom(e.Origin), InspectLink.GetTrace(e.Origin), e.Captured);
    }

    private void ReturnFocusFromInspector()
    {
        if (_inspectedFrom is { } origin && TopLevel.GetTopLevel(origin) is not null && origin.IsEffectivelyVisible)
            origin.Focus(NavigationMethod.Tab);
        _inspectedFrom = null;
    }

    private void OpenPanGlossGuide()
    {
        if (this.FindControl<Button>("HelpButton") is not { } button ||
            HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        button.Flyout?.ShowAt(button);
        help.ShowPanGlossPage();
    }

    private void OnWritingSystemsGuideClick(object? sender, RoutedEventArgs e)
    {
        if (this.FindControl<Button>("HelpButton") is not { } button ||
            HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        button.Flyout?.ShowAt(button);
        help.ShowWritingSystemsPage();
    }

    private void OnWritingSystemsSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: WritingSystemFontNotice notice } ||
            this.FindControl<Button>("SettingsButton") is not { } button) return;
        _settings?.OpenWritingSystem(notice.WritingSystemId);
        button.Flyout?.ShowAt(button);
    }

    /// <summary>The project menu's Open recent entries, one per recent project, as the menu shows them.</summary>
    public IReadOnlyList<MenuItem> RecentProjectItems =>
        this.FindControl<Button>("OpenRecentButton")?.Flyout is MenuFlyout menu
            ? menu.Items.OfType<MenuItem>().Where(item => item.CommandParameter is RecentProjectViewModel).ToList()
            : [];

    private const int RecentProjectPageSize = 20;

    private void RebuildRecentProjects(WorkspaceShellViewModel workspace, int offset = 0)
    {
        if (this.FindControl<Button>("OpenRecentButton")?.Flyout is not MenuFlyout menu) return;
        menu.Items.Clear();
        if (offset > 0)
        {
            var previous = new MenuItem { Header = $"Previous {RecentProjectPageSize}", StaysOpenOnClick = true };
            previous.Click += (_, _) => ChangePage(previous, Math.Max(0, offset - RecentProjectPageSize), false);
            menu.Items.Add(previous);
        }
        foreach (var recent in workspace.RecentProjects.Skip(offset).Take(RecentProjectPageSize))
        {
            var item = new MenuItem
            {
                Header = recent.Name,
                Command = workspace.OpenRecentProjectCommand,
                CommandParameter = recent,
            };
            ToolTip.SetTip(item, recent.FullFwDataPath);
            Avalonia.Automation.AutomationProperties.SetName(item, recent.AutomationName);
            // Close after the command consumes Click; pinned by `NewProjectsAppearAndMissingProjectsLeaveOpenRecent`.
            item.AddHandler(MenuItem.ClickEvent, (_, _) => HideProjectMenu(), handledEventsToo: true);
            menu.Items.Add(item);
        }
        var remaining = workspace.RecentProjects.Count - offset - RecentProjectPageSize;
        if (remaining > 0)
        {
            var next = new MenuItem { Header = $"Show {Math.Min(remaining, RecentProjectPageSize)} more", StaysOpenOnClick = true };
            next.Click += (_, _) => ChangePage(next, offset + RecentProjectPageSize, true);
            menu.Items.Add(next);
        }

        void ChangePage(MenuItem clicked, int pageOffset, bool forward)
        {
            var restoreFocus = clicked.IsFocused;
            Dispatcher.UIThread.Post(() =>
            {
                RebuildRecentProjects(workspace, pageOffset);
                if (!restoreFocus) return;
                var items = menu.Items.OfType<MenuItem>().ToArray();
                var prefix = forward ? "Show " : "Previous ";
                var target = items.FirstOrDefault(item => item.Header is string header &&
                    header.StartsWith(prefix, StringComparison.Ordinal)) ?? items.FirstOrDefault();
                target?.BringIntoView();
                target?.Focus();
            });
        }
    }

    [KeyboardShortcutHandler(
        "Window:ShowHelp", "Window:Back", "Window:ShowShortcuts", "Window:OpenSettings",
        "Window:ZoomIn", "Window:ZoomOut", "Window:ResetZoom", "Window:Copy", "Window:Undo", "Window:Redo",
        "TextReader:FocusWordSearch", "WordList:FocusWordSearch", "Lists:FocusWordSearch", "Matrix:FocusWordSearch")]
    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var modifiers = e.KeyModifiers;
        var scope = SearchScope();
        var windowEntry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Window, e.Key, modifiers);
        var inspectorIsOpen = DataContext is WorkspaceShellViewModel { Inspector.IsOpen: true };
        var entry = inspectorIsOpen && windowEntry?.Behavior == KeyboardShortcutBehavior.Back
            ? windowEntry
            : KeyboardShortcutRegistry.Find(scope, e.Key, modifiers) ?? windowEntry;
        if (entry is not null && (entry.Behavior is KeyboardShortcutBehavior.Copy or KeyboardShortcutBehavior.Undo or
            KeyboardShortcutBehavior.Redo) && KeyboardShortcutRegistry.Allows(entry,
                KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true))
        {
            e.Handled = true;
            switch (entry.Behavior)
            {
                case KeyboardShortcutBehavior.Copy:
                    if (KeyboardShortcutRegistry.CopyTextFor(e.Source) is { } text)
                        await _problemReportServices.CopyTextAsync(text, this).ConfigureAwait(true);
                    break;
                case KeyboardShortcutBehavior.Undo when DataContext is WorkspaceShellViewModel workspace:
                    ShowKeyboardStatus(await workspace.PageModel<ReviewPageModel>().Changes
                        .UndoStagingActionAsync().ConfigureAwait(true));
                    break;
                case KeyboardShortcutBehavior.Redo when DataContext is WorkspaceShellViewModel workspace:
                    ShowKeyboardStatus(await workspace.PageModel<ReviewPageModel>().Changes
                        .RedoStagingActionAsync().ConfigureAwait(true));
                    break;
            }
            return;
        }
        if (entry?.Behavior == KeyboardShortcutBehavior.ShowShortcuts
            && KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true))
        {
            e.Handled = true;
            this.FindControl<Button>("HelpButton")?.Flyout?.Hide();
            _settings?.OpenGroup(ViewModels.SettingsGroup.KeyboardShortcuts);
            if (this.FindControl<Button>("SettingsButton") is { } settingsButton)
                settingsButton.Flyout?.ShowAt(settingsButton);
            return;
        }
        if (entry?.Behavior == KeyboardShortcutBehavior.OpenSettings)
        {
            e.Handled = true;
            if (this.FindControl<Button>("SettingsButton") is { } settingsButton)
                settingsButton.Flyout?.ShowAt(settingsButton);
            return;
        }
        if (entry?.Behavior == KeyboardShortcutBehavior.FocusWordSearch)
        {
            if (FindWordSearch(scope) is { } search) search.Focus();
            e.Handled = true;
            return;
        }
        if (_settings is { } settings)
        {
            var zoomEntry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Window, e.Key, modifiers,
                targetBehaviors: [KeyboardShortcutBehavior.ZoomIn, KeyboardShortcutBehavior.ZoomOut,
                    KeyboardShortcutBehavior.ResetZoom]);
            if (zoomEntry?.Behavior == KeyboardShortcutBehavior.ZoomIn)
            {
                e.Handled = true;
                settings.ZoomIn();
                return;
            }
            if (zoomEntry?.Behavior == KeyboardShortcutBehavior.ZoomOut)
            {
                e.Handled = true;
                settings.ZoomOut();
                return;
            }
            if (zoomEntry?.Behavior == KeyboardShortcutBehavior.ResetZoom)
            {
                e.Handled = true;
                settings.ResetZoomCommand.Execute(null);
                return;
            }
        }
        // The inspector is the deepest step of the drill-down, so Esc steps it back before anything on the page.
        if (entry?.Behavior == KeyboardShortcutBehavior.Back &&
            DataContext is WorkspaceShellViewModel { Inspector.IsOpen: true } shell)
        {
            e.Handled = true;
            shell.Inspector.Back();
            return;
        }
        if (entry?.Behavior != KeyboardShortcutBehavior.ShowHelp) return;
        e.Handled = true;
        _helpAutomationId = e.Source is Control control
            ? Avalonia.Automation.AutomationProperties.GetAutomationId(control)
            : null;
        RefreshHelpContent();
        if (this.FindControl<Button>("HelpButton") is { } button)
            button.Flyout?.ShowAt(button);
    }

    internal void ShowKeyboardStatus(string? message)
    {
        if (this.FindControl<TextBlock>("KeyboardShortcutStatusLine") is not { } line) return;
        _keyboardStatusTimer.Stop();
        line.Text = message ?? string.Empty;
        line.IsVisible = !string.IsNullOrWhiteSpace(message);
        if (line.IsVisible) _keyboardStatusTimer.Start();
    }

    private KeyboardShortcutScope SearchScope()
    {
        if (DataContext is not WorkspaceShellViewModel shell) return KeyboardShortcutScope.Window;
        if (shell.CurrentPage != WorkspacePage.Texts) return KeyboardShortcutScope.Window;
        return shell.PageModel<TextsPageModel>().Tab switch
        {
            TextsTab.AnalyzeTexts when shell.PageModel<TextsPageModel>().AnalyzeView == AnalyzeTextsView.TextReader => KeyboardShortcutScope.TextReader,
            TextsTab.AnalyzeTexts => KeyboardShortcutScope.WordList,
            TextsTab.Lists => KeyboardShortcutScope.Lists,
            TextsTab.Matrix => KeyboardShortcutScope.Matrix,
            _ => KeyboardShortcutScope.Window,
        };
    }

    private TextBox? FindWordSearch(KeyboardShortcutScope scope) => this.GetVisualDescendants().OfType<TextBox>()
        .FirstOrDefault(box => box.IsEffectivelyVisible &&
            (scope switch
            {
                KeyboardShortcutScope.TextReader => box.Name == "ReaderWordSearch",
                KeyboardShortcutScope.WordList => box.Name == "WordListSearch",
                KeyboardShortcutScope.Lists => box.Name == "ListsWordSearch",
                KeyboardShortcutScope.Matrix => box.Name == "MatrixWordSearch",
                _ => false,
            }));

    private void OnHelpFlyoutOpened(object? sender, EventArgs e)
    {
        RefreshHelpContent();
        _helpAutomationId = null;
    }

    private void ConfigureSettings(WorkspaceShellViewModel workspace)
    {
        _settings = new SettingsViewModel(workspace, _preferences, _applicationFacts);
        if (SettingsPopup is { } settingsPopup)
        {
            settingsPopup.DataContext = _settings;
            settingsPopup.CloseRequested += (_, _) => HideSettingsFlyout();
            settingsPopup.OpenHelpRequested += (_, _) => OpenHelpFromSettings();
            settingsPopup.ReportProblemRequested += async (_, _) =>
            {
                HideSettingsFlyout();
                await ShowProblemReportAsync(ProblemReport.ForWindowAction(_applicationFacts)).ConfigureAwait(true);
            };
            settingsPopup.OpenSiteRequested += async (_, _) =>
            {
                HideSettingsFlyout();
                await _uriLauncher.LaunchAsync(new Uri(AppLinks.Documentation)).ConfigureAwait(true);
            };
            settingsPopup.ConfigureRequested += (_, _) =>
            {
                HideSettingsFlyout();
                workspace.ConfigureCommand.Execute(null);
            };
            settingsPopup.OpenDataFolderRequested += async (_, _) =>
            {
                HideSettingsFlyout();
                var path = Path.GetFullPath(_settings.DataRoot);
                await _uriLauncher.LaunchAsync(new Uri(path)).ConfigureAwait(true);
            };
            settingsPopup.CopyVersionsRequested += async (_, _) =>
            {
                await _problemReportServices.CopyTextAsync(
                    ProblemReport.ForWindowAction(_applicationFacts).ToVersionText(), this)
                    .ConfigureAwait(true);
                _settings.MarkVersionsCopied();
            };
        }

        if (this.FindControl<LayoutTransformControl>("RootLayoutTransform") is { } transformRoot)
        {
            WindowZoomPolicy.Apply(Application.Current!, _settings.ZoomPercent);
            transformRoot.LayoutTransform = WindowZoomPolicy.Transform;
            _settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(SettingsViewModel.ZoomScale)) return;
                WindowZoomPolicy.Apply(Application.Current!, _settings.ZoomPercent);
                workspace.UpdateWindowWidth(Width / _settings.ZoomScale);
                UpdateSettingsFlyoutZoom();
                UpdateSetupDialogZoom();
            };
        }
        UpdateSettingsFlyoutZoom();
        UpdateSetupDialogZoom();
    }

    private void OpenHelpFromSettings()
    {
        HideSettingsFlyout();
        RefreshHelpContent();
        if (this.FindControl<Button>("HelpButton") is { } button) button.Flyout?.ShowAt(button);
    }

    private void OnSettingsFlyoutOpened(object? sender, EventArgs e)
    {
        UpdateSettingsFlyoutZoom();
        if (this.FindControl<Button>("SettingsButton") is { } button) button.Classes.Add("open");
        if (_settings?.TakeWritingSystemTargetId() is { } writingSystemId)
            SettingsPopup?.FocusWritingSystem(writingSystemId);
        else if (_settings?.IsKeyboardShortcutsSelected == true) SettingsPopup?.FocusShortcutSearch();
        else SettingsPopup?.FocusSelectedGroup();
    }

    private void RepositionSettingsFlyout()
    {
        if (this.FindControl<Button>("SettingsButton") is not { Flyout: { IsOpen: true } flyout } button) return;
        flyout.Hide();
        flyout.ShowAt(button);
    }

    private void UpdateSettingsFlyoutZoom()
    {
        if (_settings is not { } settings || SettingsPopup is not { } popup ||
            this.FindControl<Button>("SettingsButton")?.Flyout is not { IsOpen: true } ||
            Application.Current?.TryGetResource("Intent.Space.Section", ActualThemeVariant, out var insetValue) != true ||
            insetValue is not double inset || !double.IsFinite(Width) || !double.IsFinite(Height))
            return;
        var zoom = settings.ZoomScale;
        var topBarHeight = this.FindControl<Border>("TopBar")?.Bounds.Height ?? 0;
        var maxLogicalWidth = Math.Max(0, Width / zoom - inset * 2);
        var maxLogicalHeight = Math.Max(0, Height / zoom - topBarHeight - inset);
        popup.FitToAvailableArea(maxLogicalWidth, maxLogicalHeight);
    }

    private void UpdateSetupDialogZoom()
    {
        if (_settings is not { } settings || this.FindControl<SetupDialog>("SetupDialogView") is not { } dialog ||
            Application.Current?.TryGetResource("Intent.Space.Section", ActualThemeVariant, out var insetValue) != true ||
            insetValue is not double inset || !double.IsFinite(Width) || !double.IsFinite(Height))
            return;
        var zoom = settings.ZoomScale;
        var maxLogicalWidth = Math.Max(0, Width / zoom - inset * 2);
        var maxLogicalHeight = Math.Max(0, Height / zoom - inset * 2);
        dialog.FitToAvailableArea(maxLogicalWidth, maxLogicalHeight);
    }

    private void OnSettingsFlyoutClosed(object? sender, EventArgs e)
    {
        if (this.FindControl<Button>("SettingsButton") is { } button)
        {
            button.Classes.Remove("open");
            button.Focus(NavigationMethod.Tab);
        }
    }

    private void HideSettingsFlyout() => this.FindControl<Button>("SettingsButton")?.Flyout?.Hide();

    private void RefreshHelpContent()
    {
        if (HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        var page = (DataContext as WorkspaceShellViewModel)?.CurrentPage ?? WorkspacePage.Overview;
        help.ShowForPage(page, _helpAutomationId);
    }

    // Hiding unbinds the entry; pinned by `ConfigureOpensOnceFromKeyboardAndDoubleClick`.
    private void OnProjectMenuEntryClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(HideProjectMenu);

    private void HideProjectMenu() => this.FindControl<Button>("ProjectMenuButton")?.Flyout?.Hide();

    private void RestoreBounds()
    {
        var bounds = _preferences.Current.Window;
        if (bounds is null) return;
        Width = Math.Max(bounds.Width, MinWidth);
        Height = Math.Max(bounds.Height, MinHeight);
        if (bounds.X is { } x && bounds.Y is { } y) Position = new PixelPoint(x, y);
    }

    private void SaveBounds()
    {
        _preferences.Update(preferences => preferences with
        {
            Window = new WindowPlacement(Width, Height, Position.X, Position.Y),
        });
    }

    private void ConstrainRestoredBounds()
    {
        if (_preferences.Current.Window is not { X: not null, Y: not null }) return;
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        if (screen is null) return;
        Position = WindowPlacementPolicy.Constrain(Position, screen.WorkingArea,
            new Size(Width, Height), screen.Scaling);
    }
}
