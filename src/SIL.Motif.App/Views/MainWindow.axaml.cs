using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
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
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The window frame holds the title bar, navigation rail, page panels and status bar. It stays minimal
/// until <see cref="Compose"/> supplies its workspace and builds the pages, so a caller can construct and
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
    private readonly Dictionary<WorkspacePage, Control> _pageViews = [];
    private Panel? _pageHost;
    private readonly WorkspaceLayoutHost? _layoutHost;
    private HelpPopupView? HelpPopup =>
        this.FindControl<Button>("HelpButton")?.Flyout is Flyout flyout ? flyout.Content as HelpPopupView : null;
    private WorkspacePage _settingsReturnPage = WorkspacePage.Overview;

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
    /// <param name="applicationFacts">Application details used for problem reports.</param>
    public MainWindow(bool rememberBounds, IUriLauncher? uriLauncher, IUserPreferencesStore? preferencesStore,
        ApplicationFacts? applicationFacts = null)
    {
        _preferences = preferencesStore ?? new MemoryUserPreferencesStore();
        _applicationFacts = applicationFacts ?? ApplicationFacts.Current;
        AvaloniaXamlLoader.Load(this);
        _layoutHost = this.FindControl<WorkspaceLayoutHost>("WorkspaceLayout");
        if (_layoutHost is { } budgetHost && this.FindControl<ScrollViewer>("TechDemoNoticeViewport") is { } noticeViewport)
            noticeViewport.Bind(MaxHeightProperty, budgetHost.GetObservable(WorkspaceLayoutHost.BandBudgetProperty));
        if (_layoutHost is { } layoutHost)
            layoutHost.AttachedToVisualTree += (_, _) => UpdateFrameViewport(ClientSize.Width / (_settings?.ZoomScale ?? 1));
        SizeChanged += (_, e) =>
        {
            UpdateFrameViewport(e.NewSize.Width / (_settings?.ZoomScale ?? 1));
        };
        _keyboardStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _keyboardStatusTimer.Tick += (_, _) =>
        {
            _keyboardStatusTimer.Stop();
            ShowKeyboardStatus(null);
        };
        _uriLauncher = uriLauncher ?? new AvaloniaLauncher(this);
        _problemReportServices = ProblemReportWindowServices.ForWindow(this, _uriLauncher);
        ConfigureWindowChrome();
        if (HelpPopup is { } helpView)
            helpView.DataContext = new HelpPopupViewModel(_uriLauncher);
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (this.FindControl<Menu>("MainMenu") is { } mainMenu) MenuShortcutDisplay.Apply(mainMenu);
        AddHandler(GotFocusEvent, RememberFocusOutsideMenu, RoutingStrategies.Bubble);
        // A command handles Click, so the close needs handledEventsToo.
        foreach (var name in new[] { "SelectNewMenuItem", "ConfigureMenuItem" })
            this.FindControl<MenuItem>(name)?.AddHandler(MenuItem.ClickEvent, (_, _) => HideProjectMenu(), handledEventsToo: true);
        if (this.FindControl<Menu>("MainMenu") is { } menuBar)
            foreach (var item in menuBar.GetLogicalDescendants().OfType<MenuItem>()) ActivateOnSpace(item);
        Closed += (_, _) => FlushPreferences();
        AddHandler(InputElement.KeyDownEvent, OnMenuAccessKeyDown, RoutingStrategies.Tunnel);
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

    private void ConfigureWindowChrome()
    {
        var chromeClass = OperatingSystem.IsWindows() ? "windowsChrome"
            : OperatingSystem.IsMacOS() ? "macChrome"
            : null;
        if (chromeClass is null) return;
        ExtendClientAreaToDecorationsHint = true;
        ExtendClientAreaTitleBarHeightHint = -1;
        if (this.FindControl<Border>("TopBar") is { } topBar) topBar.Classes.Add(chromeClass);
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        using var span = MotifTrace.BeginLayout("layout.measure");
        return base.MeasureOverride(availableSize);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        using var span = MotifTrace.BeginLayout("layout.arrange");
        return base.ArrangeOverride(finalSize);
    }

    /// <summary>Builds each page's view from its model with <see cref="PageRegistry"/>, and binds the window to <paramref name="workspace"/>.</summary>
    public void Compose(WorkspaceShellViewModel workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        DataContext = workspace;
        foreach (var name in new[] { "NoticeDetailsButton", "StatusParseProgress" })
            if (this.FindControl<Button>(name)?.Flyout is Flyout { Content: Control content })
                content.DataContext = workspace;
        ConfigureSettings(workspace);
        WritingSystemText.SetResolver(this, workspace.Context.TextStyles);

        var host = this.FindControl<Panel>("PageHost")
            ?? throw new InvalidOperationException("MainWindow.axaml has no element named 'PageHost'.");
        workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuide = OpenPanGlossGuide;
        _pageHost = host;
        EnsurePageView(workspace, workspace.CurrentPage);

        void UpdatePagePanels()
        {
            using var span = MotifTrace.BeginInSwitch("switch.view-attach");
            this.FindControl<Button>("SettingsButton")?.Classes.Set("open", workspace.CurrentPage == WorkspacePage.Settings);
            _layoutHost?.SetPagePanels(
                _pageViews.GetValueOrDefault(workspace.CurrentPage) as IWorkspacePaneProvider);
            _layoutHost?.ShowWorkingPage();
            if (this.FindControl<ListBox>("PageList") is not { } pageRail) return;
            var current = pageRail.Items.OfType<PageViewModel>().FirstOrDefault(page => page.Page == workspace.CurrentPage);
            pageRail.SetCurrentValue(ListBox.SelectedItemProperty, current);
        }
        workspace.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(WorkspaceShellViewModel.CurrentPage))
            {
                EnsurePageView(workspace, workspace.CurrentPage);
                UpdatePagePanels();
                MotifTrace.EndAtFirstFrame(this);
            }
            UpdateNoticeBand();
        };
        workspace.PageModel<TextsPageModel>().PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TextsPageModel.Tab)) MotifTrace.EndAtFirstFrame(this);
        };
        workspace.Project.PropertyChanged += (_, _) => UpdateNoticeBand();
        workspace.Context.TextStyles.MissingFontNotices.CollectionChanged += (_, _) => UpdateNoticeBand();
        UpdateNoticeBand();
        workspace.Inspector.PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(InspectorViewModel.IsOpen))
                _layoutHost?.SetObjectInspectorOpen(workspace.Inspector.IsOpen);
        };
        if (this.FindControl<ListBox>("PageList") is { } rail)
        {
            rail.Selection.LostSelection += (_, _) =>
            {
                var current = rail.Items.OfType<PageViewModel>().FirstOrDefault(page => page.Page == workspace.CurrentPage);
                if (current is not null) rail.SetCurrentValue(ListBox.SelectedItemProperty, current);
            };
            WorkspacePage? pressedPage = null;
            rail.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
            {
                var item = e.Source as ListBoxItem ?? (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
                pressedPage = item?.DataContext is PageViewModel { IsCurrent: true } page ? page.Page : null;
            }, RoutingStrategies.Tunnel);
            rail.AddHandler(InputElement.PointerReleasedEvent, (_, _) =>
            {
                if (pressedPage == workspace.CurrentPage) _layoutHost?.ToggleSidePanel();
                pressedPage = null;
            }, RoutingStrategies.Bubble, handledEventsToo: true);
            rail.AddHandler(InputElement.KeyDownEvent, OnRailKeyDown, RoutingStrategies.Tunnel);
        }
        UpdatePagePanels();
        _layoutHost?.SetObjectInspectorOpen(workspace.Inspector.IsOpen);
        if (this.FindControl<Button>("StatusChanges") is { } changesStatus)
            changesStatus.DataContext = workspace.PageOf(WorkspacePage.Review);
        if (this.FindControl<Button>("StatusWarnings") is { } warningsStatus)
            warningsStatus.DataContext = workspace.PageOf(WorkspacePage.Warnings);
        UpdateFrameViewport(Width / (_settings?.ZoomScale ?? 1));

        AddHandler(InspectLink.RequestedEvent, (_, e) => OnInspectRequested(workspace, e));
        AddHandler(WorkspaceLayoutHost.PageInspectorRequestedEvent,
            (_, e) => OnPageInspectorRequested(workspace, e), RoutingStrategies.Bubble);
        workspace.Inspector.Closed += (_, _) => ReturnFocusFromInspector();

        if (this.FindControl<ProjectCardView>("ProjectCardView") is { } card) card.DataContext = workspace;
        ConfigureProjectCard();
        SizeChanged += (_, _) =>
        {
            CloseTransientTooltips();
            UpdateSetupDialogZoom();
        };
        if (this.FindControl<MenuItem>("FileMenu") is { } fileMenu)
            fileMenu.SubmenuOpened += (_, _) => _ = workspace.RefreshKnownProjectsAsync();
        // Coming back from FieldWorks is when a save it made is news; this reads, it never reruns.
        Activated += (_, _) => _ = workspace.CheckFreshnessAsync();
        workspace.RecentProjects.CollectionChanged += (_, _) => RebuildRecentProjects(workspace);
        RebuildRecentProjects(workspace);
        WindowZoomPolicy.EnablePopupTransforms(this);
    }

    private void UpdateFrameViewport(double width)
    {
        if (_layoutHost is not { } host || TopLevel.GetTopLevel(host) != this) return;
        host.UpdateViewport(width);
        if (DataContext is WorkspaceShellViewModel workspace)
        {
            workspace.IsSidebarCollapsed = true;
            workspace.IsChromeCompact = width < (double)this.FindResource("Component.TopBar.CompactWidth")!;
        }
        if (this.FindControl<Border>("TopBar") is { } topBar)
        {
            var narrow = width < (double)this.FindResource("Component.TopBar.NarrowWidth")!;
            topBar.Classes.Set("narrow", narrow);
            SetMenusCollapsed(narrow);
        }
        if (this.FindControl<Border>("StatusBar") is { } status)
            status.Classes.Set("compact", width < (double)this.FindResource("Component.StatusBar.CompactWidth")!);
        var shortRail = Height / (_settings?.ZoomScale ?? 1) < (double)this.FindResource("Component.Sidebar.ShortHeight")!;
        this.FindControl<ListBox>("PageList")?.Classes.Set("shortRail", shortRail);
        this.FindControl<StackPanel>("SidebarFooter")?.Classes.Set("shortRail", shortRail);
    }

    [KeyboardShortcutHandler("NavigationRail:ToggleSidePanel")]
    private void OnRailKeyDown(object? sender, KeyEventArgs e)
    {
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.NavigationRail, e.Key, e.KeyModifiers);
        if (entry?.Behavior != KeyboardShortcutBehavior.ToggleSidePanel ||
            DataContext is not WorkspaceShellViewModel workspace) return;
        var item = e.Source as ListBoxItem ?? (e.Source as Control)?.FindAncestorOfType<ListBoxItem>();
        var isCurrentItem = item?.DataContext is PageViewModel { IsCurrent: true } page &&
            page.Page == workspace.CurrentPage;
        if (!KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), isCurrentItem)) return;
        _layoutHost?.ToggleSidePanel();
        e.Handled = true;
    }

    private void UpdateNoticeBand()
    {
        using var span = MotifTrace.Begin("shell.update-notice-band");
        MotifTrace.Count("update-notice-band");
        if (DataContext is not WorkspaceShellViewModel workspace ||
            this.FindControl<Border>("NoticeBand") is not { } band ||
            this.FindControl<TextBlock>("NoticeSummary") is not { } summary) return;
        var message = workspace.Baseline.ShownRefusal?.Sentence ?? workspace.OpenRefusal?.Sentence ??
            workspace.Project.MachineStoreRecoveryMessage ??
            workspace.Context.TextStyles.MissingFontNotices.FirstOrDefault()?.Message;
        summary.Text = message ?? string.Empty;
        ToolTip.SetTip(summary, message);
        band.IsVisible = !string.IsNullOrWhiteSpace(message);
        if (_layoutHost is not null) _layoutHost.NoticeBandVisible = band.IsVisible;
    }

    private async void OnStatusReportProblemClick(object? sender, RoutedEventArgs e) =>
        await ShowProblemReportAsync(ProblemReport.ForWindowAction(_applicationFacts)).ConfigureAwait(true);

    // Names inside object details extend the breadcrumb; other names begin a fresh inspection.
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
        _layoutHost?.ShowInspector();
    }

    private void OnPageInspectorRequested(WorkspaceShellViewModel workspace, RoutedEventArgs e)
    {
        _inspectedFrom = null;
        workspace.Inspector.Close();
        e.Handled = true;
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
        if (sender is not Control { DataContext: WritingSystemFontNotice notice }) return;
        this.FindControl<Button>("NoticeDetailsButton")?.Flyout?.Hide();
        _settings?.OpenWritingSystem(notice.WritingSystemId);
        OpenSettings();
    }

    /// <summary>The project menu's Open recent entries, one per recent project, as the menu shows them.</summary>
    public IReadOnlyList<MenuItem> RecentProjectItems =>
        this.FindControl<MenuItem>("OpenRecentMenuItem") is { } menu
            ? menu.Items.OfType<MenuItem>().Where(item => item.CommandParameter is RecentProjectViewModel).ToList()
            : [];

    private const int RecentProjectPageSize = 20;

    private void RebuildRecentProjects(WorkspaceShellViewModel workspace, int offset = 0)
    {
        if (this.FindControl<MenuItem>("OpenRecentMenuItem") is not { } menu) return;
        menu.Items.Clear();
        if (offset > 0)
        {
            var previous = new MenuItem
            {
                Header = PluralText.Format(MotifAppStrings.MenuRecentPreviousPattern, RecentProjectPageSize),
                Tag = "previous", StaysOpenOnClick = true,
            };
            previous.Click += (_, _) => ChangePage(previous, Math.Max(0, offset - RecentProjectPageSize), false);
            ActivateOnSpace(previous);
            menu.Items.Add(previous);
        }
        foreach (var recent in workspace.RecentProjects.Skip(offset).Take(RecentProjectPageSize))
        {
            var item = new MenuItem
            {
                Header = CreateRecentProjectHeader(recent, workspace),
                Command = workspace.OpenRecentProjectCommand,
                CommandParameter = recent,
            };
            ToolTip.SetTip(item, recent.FullFwDataPath);
            Avalonia.Automation.AutomationProperties.SetName(item, recent.AutomationName);
            // Close after the command consumes Click; pinned by `NewProjectsAppearAndMissingProjectsLeaveOpenRecent`.
            item.AddHandler(MenuItem.ClickEvent, (_, _) => HideProjectMenu(), handledEventsToo: true);
            ActivateOnSpace(item);
            menu.Items.Add(item);
        }
        var remaining = workspace.RecentProjects.Count - offset - RecentProjectPageSize;
        if (remaining > 0)
        {
            var next = new MenuItem
            {
                Header = PluralText.Format(MotifAppStrings.MenuRecentMorePattern, Math.Min(remaining, RecentProjectPageSize)),
                Tag = "next", StaysOpenOnClick = true,
            };
            next.Click += (_, _) => ChangePage(next, offset + RecentProjectPageSize, true);
            ActivateOnSpace(next);
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
                var tag = forward ? "next" : "previous";
                var target = items.FirstOrDefault(item => Equals(item.Tag, tag)) ?? items.FirstOrDefault();
                target?.BringIntoView();
                target?.Focus();
            });
        }
    }

    private Grid CreateRecentProjectHeader(RecentProjectViewModel recent, WorkspaceShellViewModel workspace)
    {
        var details = string.Join(" · ", new[]
        {
            recent.StateText,
            recent.HasWordformCount ? recent.WordformCountText : null,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var title = new TextBlock { Classes = { "recentProjectMenuTitle" }, Text = recent.Name };
        var summary = new TextBlock
        {
            Classes = { "recentProjectMenuMetadata" },
            Text = details,
            IsVisible = details.Length > 0,
        };
        var description = new StackPanel
        {
            Classes = { "recentProjectMenuDetails" },
            Children = { title, summary },
        };
        var folder = new HyperlinkButton
        {
            Classes = { "recentProjectMenuFolder" },
            Content = "Open folder",
            Command = workspace.OpenRecentProjectFolderCommand,
            CommandParameter = recent.FolderPath,
            IsEnabled = recent.CanOpenFolder,
        };
        Avalonia.Automation.AutomationProperties.SetName(folder, $"Open {recent.Name} folder");
        folder.AddHandler(Button.ClickEvent, (_, _) => Dispatcher.UIThread.Post(HideProjectMenu),
            RoutingStrategies.Bubble);
        folder.AddHandler(InputElement.PointerReleasedEvent, (_, eventArgs) => eventArgs.Handled = true,
            RoutingStrategies.Bubble, handledEventsToo: true);

        var header = new Grid
        {
            Classes = { "recentProjectMenuRow" },
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
        };
        header.Children.Add(description);
        Grid.SetColumn(folder, 1);
        header.Children.Add(folder);
        return header;
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
            await ExecuteEditAsync(entry.Behavior, e.Source).ConfigureAwait(true);
            return;
        }
        if (entry?.Behavior == KeyboardShortcutBehavior.ShowShortcuts
            && KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true))
        {
            e.Handled = true;
            OpenKeyboardShortcuts();
            return;
        }
        if (entry?.Behavior == KeyboardShortcutBehavior.OpenSettings)
        {
            e.Handled = true;
            OpenSettings();
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
        // An open project card is the topmost step, so Esc closes it before the Inspector or the page.
        if (entry?.Behavior == KeyboardShortcutBehavior.Back &&
            this.FindControl<Button>("ProjectChip")?.Flyout is Flyout { IsOpen: true } card)
        {
            e.Handled = true;
            card.Hide();
            return;
        }
        // The inspector is the deepest step of the drill-down, so Esc steps it back before anything on the page.
        if (entry?.Behavior == KeyboardShortcutBehavior.Back &&
            DataContext is WorkspaceShellViewModel { Inspector.IsOpen: true } shell &&
            this.FindControl<Inspector>("Inspector") is { IsEffectivelyVisible: true })
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
        ToolTip.SetTip(line, message);
        if (this.FindControl<TextBlock>("StatusSelectionSummary") is { } selection)
            selection.IsVisible = !line.IsVisible;
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

    /// <summary>Builds <paramref name="page"/>'s view on its first visit; a page nobody opens costs nothing at startup.</summary>
    internal Control EnsurePageView(WorkspaceShellViewModel workspace, WorkspacePage page)
    {
        if (_pageViews.TryGetValue(page, out var existing)) return existing;
        var entry = workspace.Pages.First(candidate => candidate.Page == page);
        var view = entry.Model is TextsPageModel texts
            ? new TextsPage(texts, _settings)
            : PageRegistry.For(page).CreateView(entry.Model);
        view.Name = $"{page}Page";
        view.Bind(IsVisibleProperty, new Binding(nameof(PageViewModel.IsCurrent)) { Source = entry });
        _pageHost!.Children.Add(view);
        _pageViews[page] = view;
        if (view is SettingsPage settingsPage) ConfigureSettingsPage(settingsPage, workspace);
        return view;
    }

    /// <summary>Builds <paramref name="page"/>'s view if it has none yet, so a test can reach it without visiting it.</summary>
    internal Control EnsurePageView(WorkspacePage page) =>
        EnsurePageView((WorkspaceShellViewModel)DataContext!, page);

    private void ConfigureSettings(WorkspaceShellViewModel workspace)
    {
        _settings = new SettingsViewModel(workspace, _preferences, _applicationFacts);
        workspace.PageModel<SettingsPageModel>().Settings = _settings;

        if (this.FindControl<LayoutTransformControl>("RootLayoutTransform") is { } transformRoot)
        {
            WindowZoomPolicy.Apply(Application.Current!, _settings.ZoomPercent);
            transformRoot.LayoutTransform = WindowZoomPolicy.Transform;
            _settings.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(SettingsViewModel.ZoomScale)) return;
                CloseTransientTooltips();
                WindowZoomPolicy.Apply(Application.Current!, _settings.ZoomPercent);
                UpdateFrameViewport(Width / _settings.ZoomScale);
                UpdateSetupDialogZoom();
            };
        }
        UpdateSetupDialogZoom();
    }

    private void CloseTransientTooltips()
    {
        foreach (var owner in this.GetVisualDescendants().OfType<Control>().Where(ToolTip.GetIsOpen).ToArray())
            ToolTip.SetIsOpen(owner, false);
    }

    private void ConfigureSettingsPage(SettingsPage page, WorkspaceShellViewModel workspace)
    {
        page.CloseRequested += (_, _) => CloseSettings();
        page.OpenHelpRequested += (_, _) =>
        {
            RefreshHelpContent();
            if (this.FindControl<Button>("HelpButton") is { } button) button.Flyout?.ShowAt(button);
        };
        page.ReportProblemRequested += async (_, _) =>
            await ShowProblemReportAsync(ProblemReport.ForWindowAction(_applicationFacts)).ConfigureAwait(true);
        page.OpenSiteRequested += async (_, _) =>
            await _uriLauncher.LaunchAsync(new Uri(AppLinks.Documentation)).ConfigureAwait(true);
        page.ConfigureRequested += (_, _) =>
        {
            CloseSettings();
            workspace.ConfigureCommand.Execute(null);
        };
        page.OpenDataFolderRequested += async (_, _) =>
        {
            if (_settings is null) return;
            await _uriLauncher.LaunchAsync(new Uri(Path.GetFullPath(_settings.DataRoot))).ConfigureAwait(true);
        };
        page.CopyVersionsRequested += async (_, _) =>
        {
            await _problemReportServices.CopyTextAsync(
                ProblemReport.ForWindowAction(_applicationFacts).ToVersionText(), this).ConfigureAwait(true);
            _settings?.MarkVersionsCopied();
        };
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings();

    private void OpenSettings()
    {
        if (DataContext is not WorkspaceShellViewModel workspace ||
            EnsurePageView(workspace, WorkspacePage.Settings) is not SettingsPage page) return;
        var alreadyOpen = workspace.CurrentPage == WorkspacePage.Settings;
        if (!alreadyOpen) _settingsReturnPage = workspace.CurrentPage;
        this.FindControl<Button>("HelpButton")?.Flyout?.Hide();
        workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
        if (alreadyOpen) _layoutHost?.ShowSidePanel();
        if (_settings?.TakeWritingSystemTargetId() is { } writingSystemId) page.FocusWritingSystem(writingSystemId);
        else if (_settings?.IsKeyboardShortcutsSelected == true) page.FocusShortcutSearch();
        else page.FocusSelectedGroup();
    }

    private void CloseSettings()
    {
        if (DataContext is not WorkspaceShellViewModel workspace) return;
        var destination = workspace.Context.CanOpenPage(_settingsReturnPage)
            ? _settingsReturnPage : WorkspacePage.Overview;
        workspace.ShowPageCommand.Execute(destination);
        this.FindControl<Button>("SettingsButton")?.Focus(NavigationMethod.Tab);
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

    private void RefreshHelpContent()
    {
        if (HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        var page = (DataContext as WorkspaceShellViewModel)?.CurrentPage ?? WorkspacePage.Overview;
        help.ShowForPage(page, _helpAutomationId);
    }


    /// <summary>Menu items choose on the registry's activation key, as a button does.</summary>
    private static void ActivateOnSpace(MenuItem item)
    {
        item.AddHandler(KeyDownEvent, OnMenuItemKey);
        item.AddHandler(KeyUpEvent, OnMenuItemKey);
    }

    [KeyboardShortcutHandler("Menu:ActivateMenuItem")]
    private static void OnMenuItemKey(object? sender, KeyEventArgs e)
    {
        if (sender is not MenuItem item || item.HasSubMenu || !item.IsEffectivelyEnabled) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Menu, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.ActivateMenuItem]);
        if (entry is null) return;
        e.Handled = true;
        if (e.RoutedEvent != KeyUpEvent) return;
        var hasCommand = item.Command is { } command && command.CanExecute(item.CommandParameter);
        if (hasCommand) item.Command!.Execute(item.CommandParameter);
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item) { Handled = hasCommand });
    }

    private MenuItem? _menuButton;
    private bool _menusCollapsed;

    private IEnumerable<MenuItem> TopLevelMenus() =>
        new[] { "FileMenu", "EditMenu", "ViewMenu", "ToolsMenu", "HelpMenu" }
            .Select(name => this.FindControl<MenuItem>(name)).OfType<MenuItem>();

    /// <summary>Narrow, the bar's one item is a ≡ holding File to Help; wide, they are the bar's own.</summary>
    private void SetMenusCollapsed(bool collapsed)
    {
        if (collapsed == _menusCollapsed || this.FindControl<Menu>("MainMenu") is not { } menu) return;
        var menus = TopLevelMenus().ToArray();
        if (menus.Length == 0) return;
        _menusCollapsed = collapsed;
        menu.Close();
        foreach (var open in menus.Append(_menuButton).OfType<MenuItem>()) open.IsSubMenuOpen = false;
        var button = _menuButton ??= BuildMenuButton();
        // An opened menu's pop-up still holds its items, so they come out first and go back in after re-parenting.
        var held = ReleaseSubItems(menus);
        menu.Items.Clear();
        button.Items.Clear();
        foreach (var item in menus) (collapsed ? button.Items : menu.Items).Add(item);
        if (collapsed) menu.Items.Add(button);
        foreach (var (parent, children) in held.AsEnumerable().Reverse())
            foreach (var child in children) parent.Items.Add(child);
    }

    private static List<(MenuItem Parent, List<object?> Children)> ReleaseSubItems(IEnumerable<MenuItem> menus)
    {
        var held = new List<(MenuItem, List<object?>)>();
        foreach (var menu in menus)
        {
            var children = menu.Items.Cast<object?>().ToList();
            held.AddRange(ReleaseSubItems(children.OfType<MenuItem>()));
            menu.Items.Clear();
            held.Add((menu, children));
        }
        return held;
    }

    private static MenuItem BuildMenuButton()
    {
        var button = new MenuItem
        {
            Header = MotifAppStrings.MainMenuButtonGlyph,
        };
        Avalonia.Automation.AutomationProperties.SetName(button, MotifAppStrings.MainMenuButtonAccessibleName);
        Avalonia.Automation.AutomationProperties.SetAutomationId(button, AutomationIds.MenuButton);
        ToolTip.SetTip(button, MotifAppStrings.MainMenuButtonAccessibleName);
        return button;
    }

    // With the menus behind the ≡, Alt+E opens the ≡ and then Edit; wide, the menu bar's own access keys do it.
    [KeyboardShortcutHandler("Window:OpenMenu")]
    private void OnMenuAccessKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_menusCollapsed || _menuButton is not { } button) return;
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Window, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.OpenMenu]);
        var match = entry is null ? null : TopLevelMenus().FirstOrDefault(item =>
            string.Equals(AccessKeyOf(item), entry.Key.ToString(), StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        e.Handled = true;
        button.Open();
        match.Open();
        match.Focus(NavigationMethod.Tab);
    }

    private static string? AccessKeyOf(MenuItem item) =>
        item.Header is string header && header.IndexOf('_') is var at and >= 0 && at + 1 < header.Length
            ? header[at + 1].ToString()
            : null;

    // The card keeps open for Refresh, whose result it shows, and closes for the commands that open something else.
    private void ConfigureProjectCard()
    {
        if (this.FindControl<Button>("ProjectChip") is not { } chip || chip.Flyout is not Flyout flyout) return;
        flyout.Closed += (_, _) => Dispatcher.UIThread.Post(() => chip.Focus());
        foreach (var name in new[] { "CardChooseButton", "CardSettingsButton", "CardOpenAnotherButton", "CardParseAllWordsButton" })
            if (this.FindControl<ProjectCardView>("ProjectCardView")?.FindControl<Button>(name) is { } action)
                action.AddHandler(Button.ClickEvent, (_, _) => flyout.Hide(), handledEventsToo: true);
    }

    private void HideProjectMenu() => this.FindControl<Menu>("MainMenu")?.Close();

    private void OnExitClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnEditMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<KeyboardShortcutBehavior>(tag, out var behavior))
            await ExecuteEditAsync(behavior, FocusedElementOutsideMenu()).ConfigureAwait(true);
    }

    private void OnViewMenuClick(object? sender, RoutedEventArgs e)
    {
        if (_settings is not { } settings || sender is not MenuItem { Tag: string tag }) return;
        switch (tag)
        {
            case nameof(KeyboardShortcutBehavior.ZoomIn): settings.ZoomIn(); break;
            case nameof(KeyboardShortcutBehavior.ZoomOut): settings.ZoomOut(); break;
            case nameof(KeyboardShortcutBehavior.ResetZoom): settings.ResetZoomCommand.Execute(null); break;
        }
    }

    private void OnHelpTopicsClick(object? sender, RoutedEventArgs e)
    {
        _helpAutomationId = null;
        RefreshHelpContent();
        if (this.FindControl<Button>("HelpButton") is { } button) button.Flyout?.ShowAt(button);
    }

    private void OnKeyboardShortcutsClick(object? sender, RoutedEventArgs e) => OpenKeyboardShortcuts();

    private void OpenKeyboardShortcuts()
    {
        this.FindControl<Button>("HelpButton")?.Flyout?.Hide();
        _settings?.OpenGroup(ViewModels.SettingsGroup.KeyboardShortcuts);
        OpenSettings();
    }

    // Opening a menu moves focus into it; the word to copy is wherever the person was working before.
    private object? FocusedElementOutsideMenu() =>
        _lastFocusOutsideMenu is { } remembered && remembered.IsAttachedToVisualTree() ? remembered : null;

    private Control? _lastFocusOutsideMenu;

    private void RememberFocusOutsideMenu(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Control focused && !focused.GetSelfAndVisualAncestors().Any(ancestor => ancestor is Menu))
            _lastFocusOutsideMenu = focused;
    }

    private async Task ExecuteEditAsync(KeyboardShortcutBehavior behavior, object? source)
    {
        switch (behavior)
        {
            case KeyboardShortcutBehavior.Copy:
                if (KeyboardShortcutRegistry.CopyTextFor(source) is { } text)
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
    }

    private void RestoreBounds()
    {
        var bounds = _preferences.Current.Window;
        if (bounds is null) return;
        Width = Math.Max(bounds.Width, MinWidth);
        Height = Math.Max(bounds.Height, MinHeight);
        if (bounds.X is { } x && bounds.Y is { } y) Position = new PixelPoint(x, y);
    }

    /// <summary>Writes preferences still waiting for their delayed save; window close and session close call it.</summary>
    internal void FlushPreferences() => _preferences.Flush();

    private void SaveBounds()
    {
        var placement = new WindowPlacement(Width, Height, Position.X, Position.Y);
        _preferences.Update(preferences => preferences with
        {
            Window = placement,
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
