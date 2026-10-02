using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private static readonly string PreferencesFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Motif", "window-bounds.json");
    private readonly IUriLauncher _uriLauncher;
    private string? _helpAutomationId;
    private Control? _inspectedFrom;
    private HelpPopupView? HelpPopup =>
        this.FindControl<Button>("HelpButton")?.Flyout is Flyout flyout ? flyout.Content as HelpPopupView : null;

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
    {
        AvaloniaXamlLoader.Load(this);
        _uriLauncher = uriLauncher ?? new AvaloniaLauncher(this);
        if (HelpPopup is { } helpView)
            helpView.DataContext = new HelpPopupViewModel(_uriLauncher);
        AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        if (this.FindControl<StackPanel>("TopBarActions") is { } actions)
            foreach (var action in actions.Children.OfType<Button>())
                action.AddHandler(ToolTip.ToolTipOpeningEvent, (_, _) => PlaceBesideTopBarActions(actions, action));
        if (!rememberBounds) return;
        RestoreBounds();
        Closing += (_, _) => SaveBounds();
    }

    private async void OnParseReportProblemClick(object? sender, RoutedEventArgs e) =>
        await _uriLauncher.LaunchAsync(new Uri(AppLinks.Issues)).ConfigureAwait(true);

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

        var host = this.FindControl<Panel>("PageHost")
            ?? throw new InvalidOperationException("MainWindow.axaml has no element named 'PageHost'.");
        workspace.PageModel<TextsPageModel>().ResultsInText.OpenPanGlossGuide = OpenPanGlossGuide;
        foreach (var entry in workspace.Pages)
        {
            var view = PageRegistry.For(entry.Page).CreateView(entry.Model);
            view.Name = $"{entry.Page}Page";
            view.Bind(IsVisibleProperty, new Binding(nameof(PageViewModel.IsCurrent)) { Source = entry });
            host.Children.Add(view);
        }

        AddHandler(InspectLink.RequestedEvent, (_, e) => OnInspectRequested(workspace, e));
        workspace.Inspector.Closed += (_, _) => ReturnFocusFromInspector();

        workspace.UpdateWindowWidth(Width);
        SizeChanged += (_, e) => workspace.UpdateWindowWidth(e.NewSize.Width);
        if (this.FindControl<Button>("ProjectMenuButton")?.Flyout is Flyout projectMenu)
            projectMenu.Opened += (_, _) => _ = workspace.RefreshKnownProjectsAsync();
        // Coming back from FieldWorks is when a save it made is news; this reads, it never reruns.
        Activated += (_, _) => _ = workspace.CheckFreshnessAsync();
        workspace.RecentProjects.CollectionChanged += (_, _) => RebuildRecentProjects(workspace);
        RebuildRecentProjects(workspace);
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

    /// <summary>The project menu's Open recent entries, one per recent project, as the menu shows them.</summary>
    public IReadOnlyList<MenuItem> RecentProjectItems =>
        this.FindControl<Button>("OpenRecentButton")?.Flyout is MenuFlyout menu
            ? menu.Items.OfType<MenuItem>().ToList()
            : [];

    private void RebuildRecentProjects(WorkspaceShellViewModel workspace)
    {
        if (this.FindControl<Button>("OpenRecentButton")?.Flyout is not MenuFlyout menu) return;
        menu.Items.Clear();
        foreach (var recent in workspace.RecentProjects)
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
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The inspector is the deepest step of the drill-down, so Esc steps it back before anything on the page.
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None &&
            DataContext is WorkspaceShellViewModel { Inspector.IsOpen: true } shell)
        {
            e.Handled = true;
            shell.Inspector.Back();
            return;
        }
        if (e.Key != Key.F1) return;
        e.Handled = true;
        _helpAutomationId = e.Source is Control control
            ? Avalonia.Automation.AutomationProperties.GetAutomationId(control)
            : null;
        RefreshHelpContent();
        if (this.FindControl<Button>("HelpButton") is { } button)
            button.Flyout?.ShowAt(button);
    }

    private void OnHelpFlyoutOpened(object? sender, EventArgs e)
    {
        RefreshHelpContent();
        _helpAutomationId = null;
    }

    private void RefreshHelpContent()
    {
        if (HelpPopup?.DataContext is not HelpPopupViewModel help) return;
        var page = (DataContext as WorkspaceShellViewModel)?.CurrentPage ?? WorkspacePage.Overview;
        help.ShowForPage(page, _helpAutomationId);
    }

    // Hiding unbinds the entry; pinned by `ConfigureOpensOnceFromKeyboardAndDoubleClick`.
    private void OnProjectMenuEntryClick(object? sender, RoutedEventArgs e) => Dispatcher.UIThread.Post(HideProjectMenu);

    private void HideProjectMenu() => this.FindControl<Button>("ProjectMenuButton")?.Flyout?.Hide();

    // A preferences read failure must not block startup; an unreadable or absent file just keeps defaults.
    private void RestoreBounds()
    {
        try
        {
            if (!File.Exists(PreferencesFilePath)) return;
            var bounds = JsonSerializer.Deserialize<WindowBounds>(File.ReadAllText(PreferencesFilePath));
            if (bounds is null) return;

            Width = Math.Max(bounds.Width, MinWidth);
            Height = Math.Max(bounds.Height, MinHeight);
            if (bounds.X is { } x && bounds.Y is { } y) Position = new PixelPoint((int)x, (int)y);
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // Falling back to the XAML-declared default bounds is preferable to a startup crash.
        }
    }

    // A preferences write failure must not block shutdown.
    private void SaveBounds()
    {
        try
        {
            var directory = Path.GetDirectoryName(PreferencesFilePath);
            if (directory is not null) Directory.CreateDirectory(directory);
            var bounds = new WindowBounds(Width, Height, Position.X, Position.Y);
            File.WriteAllText(PreferencesFilePath, JsonSerializer.Serialize(bounds));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Losing the remembered window position is preferable to a crash on close.
        }
    }

    private sealed record WindowBounds(double Width, double Height, double? X, double? Y);
}
