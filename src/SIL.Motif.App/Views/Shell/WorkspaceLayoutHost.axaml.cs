using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views.Shell;

/// <summary>
/// Places page-owned panes at fixed edges and adapts their visibility to the available width.
/// Explicit visibility changes last until the corresponding width threshold is crossed.
/// </summary>
public sealed partial class WorkspaceLayoutHost : UserControl
{
    /// <summary>A bubbling request from a page to show its details in the Inspector.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> PageInspectorRequestedEvent =
        RoutedEvent.Register<WorkspaceLayoutHost, RoutedEventArgs>(nameof(PageInspectorRequested), RoutingStrategies.Bubble);

    /// <summary>
    /// The height each fixed band above or below the page may take before it scrolls, so the page keeps its
    /// minimum height. Inherited, so a notice inside the page can read it.
    /// </summary>
    public static readonly AttachedProperty<double> BandBudgetProperty =
        AvaloniaProperty.RegisterAttached<WorkspaceLayoutHost, Control, double>(
            "BandBudget", double.PositiveInfinity, inherits: true);

    /// <summary>Reads the height a fixed band may take before it scrolls.</summary>
    public static double GetBandBudget(Control control) => control.GetValue(BandBudgetProperty);

    private bool _isAttachedToVisualTree;
    private bool _hasSidePanel;
    private bool _hasPageInspector;
    private Control? _inspectorFooter;
    private Control? _inspectorActions;
    private bool _objectInspectorOpen;
    private bool _inspectorWasVisibleBeforeObject;
    private bool? _inspectorOverrideBeforeObject;
    private bool? _sideOverrideBeforeObject;
    private bool? _sidePanelOpenOverride;
    private bool? _inspectorOpenOverride;
    private double _viewportWidth = double.PositiveInfinity;

    public WorkspaceLayoutHost()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(PageInspectorRequestedEvent, OnPageInspectorRequested, RoutingStrategies.Bubble);
        foreach (var rowHost in new[] { "InspectorRowHost", "PageActionRowHost" })
            Part<ContentControl>(rowHost).SizeChanged += (_, _) => InvalidateMeasure();
        AttachedToVisualTree += (_, _) =>
        {
            _isAttachedToVisualTree = true;
            UpdatePanes();
        };
        DetachedFromVisualTree += (_, _) => _isAttachedToVisualTree = false;
    }

    /// <summary>Raised when a page asks to show its details in the Inspector.</summary>
    public event EventHandler<RoutedEventArgs> PageInspectorRequested
    {
        add => AddHandler(PageInspectorRequestedEvent, value, RoutingStrategies.Bubble);
        remove => RemoveHandler(PageInspectorRequestedEvent, value);
    }

    /// <summary>Raises a page-details request from <paramref name="origin"/> so the nearest host can restore its pane.</summary>
    /// <param name="origin">The page control that made the request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="origin"/> is null.</exception>
    public static void RequestPageInspector(Control origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        origin.RaiseEvent(new RoutedEventArgs(PageInspectorRequestedEvent));
    }

    /// <summary>Creates a standalone host that attaches the page's optional panes and tracks its logical width.</summary>
    /// <param name="page">The page to place in the host.</param>
    /// <returns>A host containing the page and its optional panes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="page"/> is null.</exception>
    public static WorkspaceLayoutHost ForPage(Control page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var host = new WorkspaceLayoutHost { Page = page };
        host.SetPagePanels(page as IWorkspacePaneProvider);
        host.AttachedToVisualTree += (_, _) => host.UpdateViewport(host.Bounds.Width);
        host.SizeChanged += (_, _) =>
        {
            if (host._isAttachedToVisualTree) host.UpdateViewport(host.Bounds.Width);
        };
        return host;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var footerCap = (double)this.FindResource("Component.Shell.FooterMaxHeight")!;
        var pageMinHeight = (double)this.FindResource("Component.Shell.WorkingPageMinHeight")!;
        // Fixed bands (the reveal row, the project notice band) come off first; the notice and footer split the rest.
        var paneHeader = (double)this.FindResource("Component.Shell.PaneHeaderHeight")!;
        var fixedBands = (Part<Button>("SidePanelRevealButton").IsVisible ? paneHeader : 0) +
            (Page is Control page && page.GetLogicalDescendants().OfType<Border>().Any(border =>
                border.Name == "NoticeBand" && border.IsVisible) ? paneHeader : 0);
        var bandBudget = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity
            : Math.Max(0, (availableSize.Height - pageMinHeight - fixedBands) / 2);
        var footerBudget = Math.Min(footerCap, bandBudget);
        SetValue(BandBudgetProperty, footerBudget);
        // Beside the page the Inspector column keeps its own body; the page's minimum height does not bind it.
        var inspectorBody = (double)this.FindResource("Component.Shell.InspectorBodyMinHeight")!;
        var inspectorBudget = double.IsInfinity(availableSize.Height) ? double.PositiveInfinity
            : Math.Max(0, Math.Min(footerCap, availableSize.Height - paneHeader - inspectorBody));
        FitFooter("InspectorFooterViewport", "InspectorRowHost", inspectorBudget);
        FitFooter("PageActionFooterViewport", "PageActionRowHost", footerBudget);
        return base.MeasureOverride(availableSize);
    }

    // The scrolling details take what the pinned actions leave, never less than their minimum.
    private void FitFooter(string viewportName, string actionsName, double budget)
    {
        var viewport = Part<ScrollViewer>(viewportName);
        var actions = Part<ContentControl>(actionsName);
        if (actions.Content is not Control row)
        {
            viewport.MaxHeight = budget;
            return;
        }
        var minimum = (double)this.FindResource("Component.Shell.FooterDetailsMinHeight")!;
        viewport.MaxHeight = double.IsInfinity(budget) ? budget : Math.Max(minimum, budget - ActionsHeight(actions, row));
    }

    // Measured from the last arrange, so fitting the details cannot feed back on itself.
    private static double ActionsHeight(ContentControl actions, Control row) =>
        row.IsEffectivelyVisible ? actions.Bounds.Height : 0;

    /// <summary>Toggles the current page's side panel through a navigation-rail action.</summary>
    public void ToggleSidePanel()
    {
        if (!_hasSidePanel) return;
        _sidePanelOpenOverride = !Part<Border>("SidePanelBorder").IsVisible;
        if (_sidePanelOpenOverride == true && !BothPanelsFit()) _inspectorOpenOverride = false;
        UpdatePanes();
    }

    /// <summary>Shows the current page's side panel, including when width rules initially collapsed it.</summary>
    public void ShowSidePanel()
    {
        if (!_hasSidePanel) return;
        _sidePanelOpenOverride = true;
        if (!BothPanelsFit()) _inspectorOpenOverride = false;
        UpdatePanes();
    }

    /// <summary>The persistent navigation rail, supplied by the shell.</summary>
    public Control? Rail
    {
        get => Part<ContentControl>("RailHost").Content as Control;
        set => Part<ContentControl>("RailHost").Content = value;
    }

    /// <summary>The working page and its notice line, supplied by the shell.</summary>
    public Control? Page
    {
        get => Part<ContentControl>("WorkingPageHost").Content as Control;
        set => Part<ContentControl>("WorkingPageHost").Content = value;
    }

    /// <summary>The shared object Inspector displayed while an object inspection is open.</summary>
    public Control? ObjectInspector
    {
        get => Part<ContentControl>("ObjectInspectorHost").Content as Control;
        set => Part<ContentControl>("ObjectInspectorHost").Content = value;
    }

    /// <summary>Replaces the current page's optional panes and detaches the preceding page's controls.</summary>
    public void SetPagePanels(IWorkspacePaneProvider? provider)
    {
        Part<ContentControl>("SidePanelContent").Content = null;
        Part<ContentControl>("PageInspectorHost").Content = null;
        Part<ContentControl>("InspectorFooterHost").Content = null;
        Part<ContentControl>("PageActionFooterHost").Content = null;
        Part<ContentControl>("InspectorRowHost").Content = null;
        Part<ContentControl>("PageActionRowHost").Content = null;
        var side = provider?.SidePanel;
        var inspector = provider?.InspectorContent;
        var footer = provider?.InspectorFooter;
        Part<TextBlock>("SidePanelTitle").Text = provider?.SidePanelTitle.ToUpperInvariant();
        Part<ContentControl>("SidePanelContent").Content = side;
        Part<ContentControl>("PageInspectorHost").Content = inspector;
        _inspectorFooter = footer;
        _inspectorActions = provider?.InspectorActions;
        _hasSidePanel = side is not null;
        _hasPageInspector = inspector is not null;
        if (!_isAttachedToVisualTree)
        {
            Part<ContentControl>("InspectorFooterHost").Content = footer;
            Part<ContentControl>("InspectorRowHost").Content = _inspectorActions;
        }
        UpdatePanes();
    }

    /// <summary>Updates whether the shared object Inspector has a subject.</summary>
    public void SetObjectInspectorOpen(bool isOpen)
    {
        if (isOpen && !_objectInspectorOpen)
        {
            _inspectorWasVisibleBeforeObject = Part<Border>("InspectorPane").IsVisible;
            _inspectorOverrideBeforeObject = _inspectorOpenOverride;
            _sideOverrideBeforeObject = _sidePanelOpenOverride;
        }
        else if (!isOpen && _objectInspectorOpen && !InspectorFitsBesidePage() &&
                 !_inspectorWasVisibleBeforeObject)
        {
            _inspectorOpenOverride = _inspectorOverrideBeforeObject;
            _sidePanelOpenOverride = _sideOverrideBeforeObject;
        }
        _objectInspectorOpen = isOpen;
        UpdatePanes();
    }

    /// <summary>Shows the Inspector pane, closing the side panel when both panes cannot fit.</summary>
    public void ShowInspector()
    {
        if (_objectInspectorOpen && !Part<Border>("InspectorPane").IsVisible)
        {
            _inspectorWasVisibleBeforeObject = false;
            _inspectorOverrideBeforeObject = _inspectorOpenOverride;
            _sideOverrideBeforeObject = _sidePanelOpenOverride;
        }
        _inspectorOpenOverride = true;
        if (!BothPanelsFit()) _sidePanelOpenOverride = false;
        UpdatePanes();
    }

    /// <summary>Reveals the working page when navigation leaves a narrow focused Inspector.</summary>
    public void ShowWorkingPage()
    {
        if (InspectorFitsBesidePage()) return;
        _inspectorOpenOverride = false;
        UpdatePanes();
    }

    /// <summary>Updates effective visibility for the available width in zoom-adjusted DIP.</summary>
    public void UpdateViewport(double width)
    {
        var sideThreshold = (double)this.FindResource("Component.Shell.SidePanelCollapseWidth")!;
        var inspectorThreshold = (double)this.FindResource("Component.Shell.InspectorCollapseWidth")!;
        if ((_viewportWidth >= sideThreshold) != (width >= sideThreshold)) _sidePanelOpenOverride = null;
        if ((_viewportWidth >= inspectorThreshold) != (width >= inspectorThreshold)) _inspectorOpenOverride = null;
        _viewportWidth = width;
        UpdatePanes();
    }

    private void UpdatePanes()
    {
        if (!_isAttachedToVisualTree) return;
        var sideVisible = _hasSidePanel && (_sidePanelOpenOverride ??
            _viewportWidth >= (double)this.FindResource("Component.Shell.SidePanelCollapseWidth")!);
        var inspectorVisible = (_hasPageInspector || _inspectorFooter is not null || _inspectorActions is not null || _objectInspectorOpen ||
            _inspectorOpenOverride == true) && (_inspectorOpenOverride ??
            _viewportWidth >= (double)this.FindResource("Component.Shell.InspectorCollapseWidth")!);
        if (sideVisible && inspectorVisible && !BothPanelsFit()) sideVisible = false;
        var focusedInspector = inspectorVisible && !InspectorFitsBesidePage();
        if (focusedInspector) sideVisible = false;
        var columns = Part<Grid>("PaneGrid").ColumnDefinitions;
        columns[1].Width = new GridLength(sideVisible ? (double)this.FindResource("Component.Shell.SidePanelWidth")! : 0);
        columns[2].Width = focusedInspector ? new GridLength(0) : GridLength.Star;
        columns[3].Width = focusedInspector ? GridLength.Star
            : new GridLength(inspectorVisible ? (double)this.FindResource("Component.Inspector.Width")!
                : (double)this.FindResource("Component.Shell.InspectorStripWidth")!);
        Part<Grid>("WorkingPageRegion").IsVisible = !focusedInspector;
        var returnButton = Part<Button>("InspectorHideButton");
        returnButton.Classes.Set("focusedInspectorReturn", focusedInspector);
        returnButton.Content = focusedInspector ? "Back to page" : "›";
        AutomationProperties.SetName(returnButton, focusedInspector ? "Back to page" : "Hide Inspector");
        var footerHost = Part<ContentControl>(inspectorVisible ? "InspectorFooterHost" : "PageActionFooterHost");
        var previousHost = Part<ContentControl>(inspectorVisible ? "PageActionFooterHost" : "InspectorFooterHost");
        previousHost.Content = null;
        footerHost.Content = _inspectorFooter;
        var rowHost = Part<ContentControl>(inspectorVisible ? "InspectorRowHost" : "PageActionRowHost");
        Part<ContentControl>(inspectorVisible ? "PageActionRowHost" : "InspectorRowHost").Content = null;
        rowHost.Content = _inspectorActions;
        Part<ScrollViewer>("InspectorFooterViewport").IsVisible = inspectorVisible && _inspectorFooter is not null;
        Part<ScrollViewer>("PageActionFooterViewport").IsVisible = !inspectorVisible && _inspectorFooter is not null;
        Part<Border>("SidePanelBorder").IsVisible = sideVisible;
        Part<Button>("SidePanelRevealButton").IsVisible = _hasSidePanel && !sideVisible;
        Part<Border>("InspectorPane").IsVisible = inspectorVisible;
        Part<Button>("InspectorStrip").IsVisible = !inspectorVisible;
        Part<ContentControl>("PageInspectorHost").IsVisible = _hasPageInspector && !_objectInspectorOpen;
        Part<ContentControl>("ObjectInspectorHost").IsVisible = _objectInspectorOpen;
        Part<TextBlock>("InspectorEmpty").IsVisible = !_hasPageInspector && !_objectInspectorOpen;
        // The band budget reads which bands show, so a visibility change must re-run it even at an unchanged size.
        InvalidateMeasure();
    }

    private bool InspectorFitsBesidePage() => _viewportWidth >=
        (double)this.FindResource("Component.Sidebar.RailWidth")! +
        (double)this.FindResource("Component.Inspector.Width")! +
        (double)this.FindResource("Component.Shell.WorkingPageMinWidth")!;

    private bool BothPanelsFit() => _viewportWidth >=
        (double)this.FindResource("Component.Sidebar.RailWidth")! +
        (double)this.FindResource("Component.Shell.SidePanelWidth")! +
        (double)this.FindResource("Component.Inspector.Width")! +
        (double)this.FindResource("Component.Shell.WorkingPageMinWidth")!;

    private void OnCollapseSidePanel(object? sender, RoutedEventArgs e)
    {
        _sidePanelOpenOverride = false;
        UpdatePanes();
        Part<Button>("SidePanelRevealButton").Focus();
    }

    private void OnRevealSidePanel(object? sender, RoutedEventArgs e) => ToggleSidePanel();

    private void OnHideInspector(object? sender, RoutedEventArgs e)
    {
        _inspectorOpenOverride = false;
        UpdatePanes();
        Part<Button>("InspectorStrip").Focus();
    }

    private void OnShowInspector(object? sender, RoutedEventArgs e)
    {
        ShowInspector();
    }

    private void OnPageInspectorRequested(object? sender, RoutedEventArgs e)
    {
        _objectInspectorOpen = false;
        ShowInspector();
    }

    private T Part<T>(string name) where T : Control => this.FindControl<T>(name)
        ?? throw new InvalidOperationException($"The frame has no part named {name}.");
}
