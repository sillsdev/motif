using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>
/// The workspace frame: navigation rail, page host, notice band, Inspector and status bar. A host window supplies
/// its <see cref="MotifWorkspaceHost"/> to <see cref="Attach"/>, which builds the pages; the view reads its model
/// from the inherited DataContext and stays minimal until then. Attachment is available within the App assembly.
/// </summary>
public sealed partial class MotifWorkspaceView : UserControl
{
    private readonly DispatcherTimer _keyboardStatusTimer;
    private readonly Dictionary<WorkspacePage, Control> _pageViews = [];
    private Panel? _pageHost;
    private readonly WorkspaceLayoutHost? _layoutHost;
    private MotifWorkspaceHost? _host;
    private Control? _inspectedFrom;
    private string? _helpAutomationId;
    private WorkspacePage _settingsReturnPage = WorkspacePage.Overview;

    private HelpPopupView? HelpPopup =>
        this.FindControl<Button>("HelpButton")?.Flyout is Flyout flyout ? flyout.Content as HelpPopupView : null;

    /// <summary>Builds the bare frame; <see cref="Attach"/> supplies the workspace and builds the pages.</summary>
    public MotifWorkspaceView()
    {
        AvaloniaXamlLoader.Load(this);
        _layoutHost = this.FindControl<WorkspaceLayoutHost>("WorkspaceLayout");
        if (_layoutHost is { } budgetHost && this.FindControl<ScrollViewer>("TechDemoNoticeViewport") is { } noticeViewport)
            noticeViewport.Bind(MaxHeightProperty, budgetHost.GetObservable(WorkspaceLayoutHost.BandBudgetProperty));
        _keyboardStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _keyboardStatusTimer.Tick += (_, _) =>
        {
            _keyboardStatusTimer.Stop();
            ShowKeyboardStatus(null);
        };
    }

    /// <summary>The layout host that places the rail, page and Inspector.</summary>
    internal WorkspaceLayoutHost? Layout => _layoutHost;

    private TopLevel Owner => _host?.Owner ?? throw new InvalidOperationException("The workspace view has no host yet.");

    /// <summary>Builds each page's view from its model with <see cref="PageRegistry"/> and binds the frame to <paramref name="host"/>.</summary>
    internal void Attach(MotifWorkspaceHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        var workspace = host.Workspace;
        var settings = host.Settings;
        if (HelpPopup is { } helpView)
            helpView.DataContext = new HelpPopupViewModel(host.UriLauncher);
        foreach (var name in new[] { "NoticeDetailsButton", "StatusParseProgress" })
            if (this.FindControl<Button>(name)?.Flyout is Flyout { Content: Control content })
                content.DataContext = workspace;

        var pageHost = this.FindControl<Panel>("PageHost")
            ?? throw new InvalidOperationException("MotifWorkspaceView.axaml has no element named 'PageHost'.");
        workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuide = OpenPanGlossGuide;
        _pageHost = pageHost;
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
                MotifTrace.EndAtFirstFrame(host.Owner);
            }
            UpdateNoticeBand();
        };
        workspace.PageModel<TextsPageModel>().PropertyChanged += (_, changed) =>
        {
            if (changed.PropertyName == nameof(TextsPageModel.Tab)) MotifTrace.EndAtFirstFrame(host.Owner);
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

        AddHandler(InspectLink.RequestedEvent, (_, e) => OnInspectRequested(workspace, e));
        AddHandler(WorkspaceLayoutHost.PageInspectorRequestedEvent,
            (_, e) => OnPageInspectorRequested(workspace, e), RoutingStrategies.Bubble);
        workspace.Inspector.Closed += (_, _) => ReturnFocusFromInspector();
    }

    /// <summary>Gives the layout host the window's width, or reports false while the host is not in <paramref name="window"/>.</summary>
    internal bool UpdateLayoutViewport(TopLevel window, double width)
    {
        if (_layoutHost is not { } host || TopLevel.GetTopLevel(host) != window) return false;
        host.UpdateViewport(width);
        return true;
    }

    /// <summary>Applies the status bar and rail classes that depend on the window's width and height.</summary>
    internal void UpdateChromeClasses(double width, double height)
    {
        if (this.FindControl<Border>("StatusBar") is { } status)
            status.Classes.Set("compact", width < (double)this.FindResource("Component.StatusBar.CompactWidth")!);
        var shortRail = height < (double)this.FindResource("Component.Sidebar.ShortHeight")!;
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

    private async void OnParseReportProblemClick(object? sender, RoutedEventArgs e)
    {
        if (_host is { } host)
            await host.ShowProblemReportAsync(ProblemReport.ForStalledParse(host.ApplicationFacts)).ConfigureAwait(true);
    }

    private async void OnMachineStoreReportProblemClick(object? sender, RoutedEventArgs e)
    {
        if (_host is { } host && DataContext is WorkspaceShellViewModel { Project.MachineStoreProblemReport: { } report })
            await host.ShowProblemReportAsync(report).ConfigureAwait(true);
    }

    private async void OnStatusReportProblemClick(object? sender, RoutedEventArgs e)
    {
        if (_host is { } host)
            await host.ShowProblemReportAsync(ProblemReport.ForWindowAction(host.ApplicationFacts)).ConfigureAwait(true);
    }

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

    /// <summary>Steps the Inspector back when it is open and showing; reports whether it did.</summary>
    internal bool TryStepInspectorBack()
    {
        if (DataContext is not WorkspaceShellViewModel { Inspector.IsOpen: true } shell ||
            this.FindControl<Inspector>("Inspector") is not { IsEffectivelyVisible: true }) return false;
        shell.Inspector.Back();
        return true;
    }

    private void OpenPanGlossGuide()
    {
        if (this.FindControl<Button>("HelpButton") is not { } button ||
            HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        button.Flyout?.ShowAt(button);
        help.ShowPanGlossPage();
    }

    /// <summary>Shows the writing systems page of Help.</summary>
    internal void ShowWritingSystemsGuide()
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
        _host?.Settings.OpenWritingSystem(notice.WritingSystemId);
        OpenSettings();
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

    /// <summary>The keyboard scope of the word search the current page owns, or the window scope.</summary>
    internal KeyboardShortcutScope SearchScope()
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

    /// <summary>The visible word search box of <paramref name="scope"/>, if the page has one.</summary>
    internal TextBox? FindWordSearch(KeyboardShortcutScope scope) => this.GetVisualDescendants().OfType<TextBox>()
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
        var host = _host!;
        var entry = workspace.Pages.First(candidate => candidate.Page == page);
        var view = entry.Model is TextsPageModel texts
            ? new TextsPage(texts, host.Settings)
            : PageRegistry.For(page).CreateView(entry.Model);
        view.Name = $"{page}Page";
        view.Bind(IsVisibleProperty, new Binding(nameof(PageViewModel.IsCurrent)) { Source = entry });
        _pageHost!.Children.Add(view);
        _pageViews[page] = view;
        if (view is SettingsPage settingsPage) ConfigureSettingsPage(settingsPage, host);
        return view;
    }

    /// <summary>Builds <paramref name="page"/>'s view if it has none yet, so a test can reach it without visiting it.</summary>
    internal Control EnsurePageView(WorkspacePage page) =>
        EnsurePageView((WorkspaceShellViewModel)DataContext!, page);

    private void ConfigureSettingsPage(SettingsPage page, MotifWorkspaceHost host)
    {
        page.CloseRequested += (_, _) => CloseSettings();
        page.OpenHelpRequested += (_, _) =>
        {
            RefreshHelpContent();
            if (this.FindControl<Button>("HelpButton") is { } button) button.Flyout?.ShowAt(button);
        };
        page.ReportProblemRequested += async (_, _) =>
            await host.ShowProblemReportAsync(ProblemReport.ForWindowAction(host.ApplicationFacts)).ConfigureAwait(true);
        page.OpenSiteRequested += async (_, _) =>
            await host.UriLauncher.LaunchAsync(new Uri(AppLinks.Documentation)).ConfigureAwait(true);
        page.ConfigureRequested += (_, _) =>
        {
            CloseSettings();
            host.Workspace.ConfigureCommand.Execute(null);
        };
        page.OpenDataFolderRequested += async (_, _) =>
            await host.UriLauncher.LaunchAsync(new Uri(Path.GetFullPath(host.Settings.DataRoot))).ConfigureAwait(true);
        page.CopyVersionsRequested += async (_, _) =>
        {
            await host.CopyTextAsync(ProblemReport.ForWindowAction(host.ApplicationFacts).ToVersionText())
                .ConfigureAwait(true);
            host.Settings.MarkVersionsCopied();
        };
    }

    private void OnSettingsClick(object? sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>Opens the Settings page and remembers the page to return to.</summary>
    internal void OpenSettings()
    {
        if (DataContext is not WorkspaceShellViewModel workspace ||
            EnsurePageView(workspace, WorkspacePage.Settings) is not SettingsPage page) return;
        var settings = _host?.Settings;
        var alreadyOpen = workspace.CurrentPage == WorkspacePage.Settings;
        if (!alreadyOpen) _settingsReturnPage = workspace.CurrentPage;
        this.FindControl<Button>("HelpButton")?.Flyout?.Hide();
        workspace.ShowPageCommand.Execute(WorkspacePage.Settings);
        if (alreadyOpen) _layoutHost?.ShowSidePanel();
        if (settings?.TakeWritingSystemTargetId() is { } writingSystemId) page.FocusWritingSystem(writingSystemId);
        else if (settings?.IsKeyboardShortcutsSelected == true) page.FocusShortcutSearch();
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

    /// <summary>Opens Settings at the keyboard shortcuts group.</summary>
    internal void OpenKeyboardShortcuts()
    {
        this.FindControl<Button>("HelpButton")?.Flyout?.Hide();
        _host?.Settings.OpenGroup(ViewModels.SettingsGroup.KeyboardShortcuts);
        OpenSettings();
    }

    /// <summary>Opens the Help popup for the current page; <paramref name="source"/> names the control whose help to show.</summary>
    internal void ShowHelp(object? source)
    {
        _helpAutomationId = source is Control control
            ? Avalonia.Automation.AutomationProperties.GetAutomationId(control)
            : null;
        RefreshHelpContent();
        if (this.FindControl<Button>("HelpButton") is { } button)
            button.Flyout?.ShowAt(button);
    }

    private void RefreshHelpContent()
    {
        if (HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        var page = (DataContext as WorkspaceShellViewModel)?.CurrentPage ?? WorkspacePage.Overview;
        help.ShowForPage(page, _helpAutomationId);
    }
}
