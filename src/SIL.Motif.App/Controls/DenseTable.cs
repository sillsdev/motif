using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Controls;

/// <summary>
/// The shared behaviour of the dense table, the grouped list that keeps headings, rows and actions aligned. A page
/// opts in with three steps. First, give its <see cref="ListBox"/> the <c>denseTable</c> class and call
/// <see cref="Attach"/> once. Second, put a model implementing <see cref="IDenseTableGroupHeading"/> before each group of rows in the bound
/// list. Third, set <see cref="ColumnsProperty"/> on the column header's grid and on each row template's grid,
/// so both read the same tracks. Column headings are read-only copyable text with the <c>denseTableHeading</c> class,
/// which carries their one shared style. For responsive tables, mark their common ancestor with IsWidthHost and declare
/// CompactColumns on both grids. Wrap secondary cells in plain controls marked Secondary; retain their facts in the Inspector.
/// </summary>
/// <remarks>
/// The column list is space separated. The <c>Narrow</c> and <c>Actions</c> roles take their shared component
/// widths; a <c>Component.*</c> resource key takes a page-owned track; other entries are grid lengths such as
/// <c>Auto</c>, <c>*</c> or <c>2*</c>. Component resources can be a number or a <see cref="GridLength"/>. Rows take
/// keyboard focus as the selection moves, group headings are skipped, Tab visits selected-row and group actions, and the
/// context-menu key or Alt+Down opens the focused row's actions menu.
/// </remarks>
public sealed class DenseTable
{
    private DenseTable() { }

    /// <summary>The space separated column tracks of a header or row grid, for example <c>Narrow * 2* Narrow Actions</c>.</summary>
    public static readonly AttachedProperty<string?> ColumnsProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Grid, string?>("Columns");

    /// <summary>
    /// Marks the common visual ancestor of a table's header and list. All participating grids use its width
    /// to choose one schema, and resolve star tracks against the smallest laid-out grid to align across a scrollbar.
    /// The wide schema must leave the component's minimum flexible width in every star column before it is used.
    /// </summary>
    public static readonly AttachedProperty<bool> IsWidthHostProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Control, bool>("IsWidthHost");

    /// <summary>
    /// The compact tracks, with the same number of columns as Columns. Use zero for a secondary column and put
    /// Secondary on its plain wrapper. Keep the hidden fact in the selected Inspector; retain actions and identity.
    /// Responsive schemas use fixed or star tracks, so both headers and rows resolve the same widths.
    /// </summary>
    public static readonly AttachedProperty<string?> CompactColumnsProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Grid, string?>("CompactColumns");

    /// <summary>
    /// Marks a plain cell wrapper hidden under the compact schema. Never apply this to a control with a semantic
    /// IsVisible binding: place that control inside the wrapper so width changes cannot replace its binding.
    /// </summary>
    public static readonly AttachedProperty<bool> SecondaryProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Control, bool>("Secondary");

    private static readonly AttachedProperty<Action<object>?> OpenDetailsProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, ListBox, Action<object>?>("OpenDetails");

    private static readonly AttachedProperty<bool> ConfiguredProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Grid, bool>("Configured");

    /// <summary>Sets the shared width host on the header and list's common ancestor.</summary>
    public static void SetIsWidthHost(Control control, bool value) => control.SetValue(IsWidthHostProperty, value);
    /// <summary>Gets whether a control is the shared width host.</summary>
    public static bool GetIsWidthHost(Control control) => control.GetValue(IsWidthHostProperty);
    /// <summary>Sets compact tracks on a header or row grid; both schemas must have equal column counts.</summary>
    public static void SetCompactColumns(Grid grid, string? value) => grid.SetValue(CompactColumnsProperty, value);
    /// <summary>Gets the grid's compact tracks.</summary>
    public static string? GetCompactColumns(Grid grid) => grid.GetValue(CompactColumnsProperty);
    /// <summary>Marks a plain wrapper for a fact kept in the selected Inspector when its column is hidden.</summary>
    public static void SetSecondary(Control control, bool value) => control.SetValue(SecondaryProperty, value);
    /// <summary>Gets whether this control is a secondary cell wrapper.</summary>
    public static bool GetSecondary(Control control) => control.GetValue(SecondaryProperty);

    // What a width host knows of its grids, so a grid joining or resizing costs a lookup, not a subtree walk.
    private sealed class HostState
    {
        public readonly HashSet<Grid> Grids = [];
        public readonly HashSet<Control> Cells = [];
        public readonly Dictionary<Grid, string> Schemas = [];
        public readonly Dictionary<string, int> SchemaCounts = new(StringComparer.Ordinal);
        public readonly Dictionary<Grid, double> Widths = [];
        public readonly SortedDictionary<double, int> WidthCounts = [];
        public (bool Compact, double Available)? Applied;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, HostState> States = new();

    private static readonly AttachedProperty<Control?> JoinedHostProperty =
        AvaloniaProperty.RegisterAttached<DenseTable, Control, Control?>("JoinedHost");

    private static void Configure(Grid grid)
    {
        if (!grid.GetValue(ConfiguredProperty))
        {
            grid.SetValue(ConfiguredProperty, true);
            grid.AttachedToVisualTree += (_, _) => Join(grid);
            grid.DetachedFromVisualTree += (_, _) => Leave(grid);
            grid.SizeChanged += (_, _) => OnGridSizeChanged(grid);
        }
        if (WidthHost(grid) is { } host) Join(grid, host);
        else SetTracks(grid, Parse(GetColumns(grid)));
    }

    private static Control? WidthHost(Visual visual) => visual.GetVisualAncestors().OfType<Control>()
        .FirstOrDefault(GetIsWidthHost);

    private static void Join(Grid grid)
    {
        if (GetColumns(grid) is not null && WidthHost(grid) is { } host) Join(grid, host);
    }

    private static void Join(Grid grid, Control host)
    {
        var state = States.GetOrCreateValue(host);
        Unregister(state, grid);
        if (GetColumns(grid) is not { } schema) return;
        grid.SetValue(JoinedHostProperty, host);
        state.Grids.Add(grid);
        state.Schemas[grid] = schema;
        state.SchemaCounts[schema] = state.SchemaCounts.GetValueOrDefault(schema) + 1;
        if (grid.Bounds.Width > 0) SetWidth(state, grid, grid.Bounds.Width);
        var wanted = Wanted(host, state);
        if (state.Applied != wanted) ApplyAll(state, wanted);
        else ApplyTo(grid, wanted);
    }

    private static void Leave(Grid grid)
    {
        if (grid.GetValue(JoinedHostProperty) is { } host && States.TryGetValue(host, out var state))
            Unregister(state, grid);
        grid.SetValue(JoinedHostProperty, null);
    }

    private static void Unregister(HostState state, Grid grid)
    {
        if (state.Schemas.Remove(grid, out var schema) &&
            --state.SchemaCounts[schema] == 0) state.SchemaCounts.Remove(schema);
        state.Grids.Remove(grid);
        SetWidth(state, grid, 0);
    }

    private static void SetWidth(HostState state, Grid grid, double width)
    {
        if (state.Widths.Remove(grid, out var old) && --state.WidthCounts[old] == 0) state.WidthCounts.Remove(old);
        if (width <= 0) return;
        state.Widths[grid] = width;
        state.WidthCounts[width] = state.WidthCounts.GetValueOrDefault(width) + 1;
    }

    private static void OnGridSizeChanged(Grid grid)
    {
        if (grid.GetValue(JoinedHostProperty) is not { } host || !States.TryGetValue(host, out var state)) return;
        SetWidth(state, grid, grid.Bounds.Width);
        var wanted = Wanted(host, state);
        if (state.Applied != wanted) ApplyAll(state, wanted);
    }

    private static void JoinCell(Control cell)
    {
        if (!GetSecondary(cell) || WidthHost(cell) is not { } host) return;
        var state = States.GetOrCreateValue(host);
        cell.SetValue(JoinedHostProperty, host);
        state.Cells.Add(cell);
        cell.IsVisible = state.Applied is not { Compact: true };
    }

    private static void LeaveCell(Control cell)
    {
        if (cell.GetValue(JoinedHostProperty) is { } host && States.TryGetValue(host, out var state))
            state.Cells.Remove(cell);
        cell.SetValue(JoinedHostProperty, null);
    }

    private static GridLength[] Parse(string? schema) => schema is null ? [] :
        schema.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Track).ToArray();

    private static double ResourceSize(object? resource) => resource is double size
        ? size : throw new InvalidOperationException("Dense table component sizes must be numbers.");

    private static double MinimumWideWidth(GridLength[] tracks, double spacing, double minimumFlexibleWidth)
    {
        var stars = tracks.Where(track => track.IsStar && track.Value > 0).Select(track => track.Value).ToArray();
        var flexibleWidth = stars.Length == 0 ? 0 : minimumFlexibleWidth * stars.Sum() / stars.Min();
        return tracks.Where(track => track.IsAbsolute).Sum(track => track.Value) + flexibleWidth +
            spacing * Math.Max(0, tracks.Length - 1);
    }

    /// <summary>How many times a grid's tracks have been recomputed; a test reads it to prove resizing is linear.</summary>
    internal static long GridVisits;

    // The schema every grid shares: compact when the narrowest laid-out grid cannot hold the widest wide schema.
    private static (bool Compact, double Available) Wanted(Control host, HostState state)
    {
        var available = Math.Min(host.Bounds.Width, state.WidthCounts.Count == 0 ? double.PositiveInfinity
            : state.WidthCounts.Keys.First());
        var wideSpacing = ResourceSize(Application.Current?.FindResource("Component.DenseTable.ColumnSpacing"));
        var minimumFlexibleWidth = ResourceSize(Application.Current?.FindResource("Component.DenseTable.MinFlexibleWidth"));
        var minimumWideWidth = state.SchemaCounts.Count == 0 ? 0 : Math.Max(
            ResourceSize(Application.Current?.FindResource("Component.DenseTable.CompactThreshold")),
            state.SchemaCounts.Keys.Max(schema => MinimumWideWidth(Parse(schema), wideSpacing, minimumFlexibleWidth)));
        return (available < minimumWideWidth, available);
    }

    private static void ApplyAll(HostState state, (bool Compact, double Available) wanted)
    {
        state.Applied = wanted;
        foreach (var grid in state.Grids) ApplyTo(grid, wanted);
        foreach (var cell in state.Cells) cell.IsVisible = !wanted.Compact;
    }

    // Walks the subtree once, for a host that exists before its grids attach; later changes arrive per grid.
    private static void Refresh(Control host)
    {
        var state = States.GetOrCreateValue(host);
        foreach (var grid in host.GetVisualDescendants().OfType<Grid>()
                     .Where(grid => GetColumns(grid) is not null && ReferenceEquals(WidthHost(grid), host)))
            if (!state.Grids.Contains(grid)) Join(grid, host);
        foreach (var cell in host.GetVisualDescendants().OfType<Control>()
                     .Where(control => GetSecondary(control) && ReferenceEquals(WidthHost(control), host)))
            if (!state.Cells.Contains(cell)) JoinCell(cell);
        if (state.Grids.Count == 0) return;
        ApplyAll(state, Wanted(host, state));
    }

    private static void ApplyTo(Grid grid, (bool Compact, double Available) wanted)
    {
        GridVisits++;
        var compact = wanted.Compact;
        var spacing = compact
            ? ResourceSize(Application.Current?.FindResource("Component.DenseTable.CompactColumnSpacing"))
            : ResourceSize(Application.Current?.FindResource("Component.DenseTable.ColumnSpacing"));
        var wide = Parse(GetColumns(grid));
        var narrow = Parse(GetCompactColumns(grid));
        if (narrow.Length != 0 && narrow.Length != wide.Length)
            throw new InvalidOperationException("Dense table wide and compact schemas must have equal column counts.");
        var tracks = compact && narrow.Length != 0 ? narrow : wide;
        if (tracks.Any(track => track.IsAuto))
            throw new InvalidOperationException("Responsive dense table tracks must use fixed or star widths.");
        grid.ColumnSpacing = spacing;
        var fixedWidth = tracks.Where(track => track.IsAbsolute).Sum(track => track.Value);
        var stars = tracks.Where(track => track.IsStar).Sum(track => track.Value);
        var room = Math.Max(0, wanted.Available - fixedWidth - spacing * Math.Max(0, tracks.Length - 1));
        if (stars > 0)
            tracks = tracks.Select(track => track.IsStar ? new GridLength(room * track.Value / stars) : track).ToArray();
        SetTracks(grid, tracks);
    }

    private static void SetTracks(Grid grid, GridLength[] tracks)
    {
        if (grid.ColumnDefinitions.Select(column => column.Width).SequenceEqual(tracks)) return;
        grid.ColumnDefinitions.Clear();
        foreach (var track in tracks) grid.ColumnDefinitions.Add(new ColumnDefinition(track));
    }

    /// <summary>The CSS class of the menu button the actions gesture opens.</summary>
    private const string MenuClass = "denseTableMenu";

    static DenseTable()
    {
        ColumnsProperty.Changed.AddClassHandler<Grid>((grid, _) => Configure(grid));
        CompactColumnsProperty.Changed.AddClassHandler<Grid>((grid, _) => Configure(grid));
        SecondaryProperty.Changed.AddClassHandler<Control>((control, args) =>
        {
            if (args.NewValue is not true) { LeaveCell(control); return; }
            control.AttachedToVisualTree += (_, _) => JoinCell(control);
            control.DetachedFromVisualTree += (_, _) => LeaveCell(control);
            JoinCell(control);
        });
        IsWidthHostProperty.Changed.AddClassHandler<Control>((host, args) =>
        {
            if (args.NewValue is true)
            {
                host.SizeChanged += OnHostSizeChanged;
                host.AttachedToVisualTree += OnHostAttached;
                Refresh(host);
            }
            else
            {
                host.SizeChanged -= OnHostSizeChanged;
                host.AttachedToVisualTree -= OnHostAttached;
            }
        });
    }

    private static void OnHostSizeChanged(object? sender, SizeChangedEventArgs args)
    {
        if (sender is not Control host) return;
        var state = States.GetOrCreateValue(host);
        var wanted = Wanted(host, state);
        if (state.Grids.Count > 0 && state.Applied != wanted) ApplyAll(state, wanted);
    }

    private static void OnHostAttached(object? sender, VisualTreeAttachmentEventArgs args)
    {
        if (sender is Control host) Refresh(host);
    }

    /// <summary>Sets the column tracks of <paramref name="grid"/>.</summary>
    public static void SetColumns(Grid grid, string? value) => grid.SetValue(ColumnsProperty, value);

    /// <summary>Gets the column tracks of <paramref name="grid"/>.</summary>
    public static string? GetColumns(Grid grid) => grid.GetValue(ColumnsProperty);

    /// <summary>
    /// Gives <paramref name="list"/> the table's keyboard model and keeps group headings out of it. Enter on a
    /// focused row calls <paramref name="openDetails"/> with that row, when the page shows row details.
    /// </summary>
    public static void Attach(ListBox list, Action<object>? openDetails = null)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.SetValue(OpenDetailsProperty, openDetails);
        object? selectedRow = list.SelectedItem is IDenseTableGroupHeading ? null : list.SelectedItem;
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is IDenseTableGroupHeading)
                list.SelectedItem = selectedRow is not null && Rows(list).Contains(selectedRow) ? selectedRow : null;
            else selectedRow = list.SelectedItem;
        };
        list.AddHandler(InputElement.PointerPressedEvent, OnHeadingPointerPressed, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        list.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);
        list.ContainerPrepared += (_, args) =>
        {
            var isRow = args.Container.DataContext is not IDenseTableGroupHeading;
            args.Container.Focusable = isRow;
            args.Container.IsHitTestVisible = true;
        };
        list.ContainerClearing += (_, args) =>
        {
            args.Container.Focusable = true;
            args.Container.IsHitTestVisible = true;
        };
    }

    private static void OnHeadingPointerPressed(object? sender, PointerPressedEventArgs args)
    {
        if (args.Source is not Visual visual) return;
        var ancestors = visual.GetSelfAndVisualAncestors().ToArray();
        var container = ancestors.OfType<ListBoxItem>().FirstOrDefault();
        if (container?.DataContext is IDenseTableGroupHeading && !ancestors.OfType<Button>().Any())
            args.Handled = true;
    }

    private static bool MoveTab(ListBox list, KeyboardShortcutBehavior behavior, KeyModifiers modifiers)
    {
        if (behavior is not (KeyboardShortcutBehavior.NextAction or KeyboardShortcutBehavior.PreviousAction) ||
            list.SelectedItem is not { } selected || list.ContainerFromItem(selected) is not { } row)
            return false;
        var groups = ((System.Collections.IEnumerable)(list.ItemsSource ?? list.Items)).Cast<object>()
            .Where(item => item is IDenseTableGroupHeading)
            .Select(list.ContainerFromItem).OfType<ListBoxItem>();
        var stops = new List<InputElement> { row };
        stops.AddRange(row.GetVisualDescendants().OfType<Button>().Where(TabAction));
        stops.AddRange(groups.SelectMany(group => group.GetVisualDescendants().OfType<Button>().Where(TabAction)));
        var focused = TopLevel.GetTopLevel(list)?.FocusManager?.GetFocusedElement();
        var index = stops.FindIndex(stop => ReferenceEquals(stop, focused));
        if (index < 0) return false;
        var next = index + (behavior == KeyboardShortcutBehavior.PreviousAction ? -1 : 1);
        if ((uint)next >= (uint)stops.Count) return false;
        stops[next].Focus(NavigationMethod.Tab, modifiers);
        return true;
    }

    private static bool TabAction(Button button) =>
        button.Focusable && button.IsTabStop && button.IsEffectivelyVisible && button.IsEffectivelyEnabled;

    private static GridLength Track(string track)
    {
        var app = Application.Current;
        var value = track switch
        {
            "Narrow" => app?.FindResource("Component.DenseTable.Column.NarrowWidth"),
            "Actions" => app?.FindResource("Component.DenseTable.Column.ActionsWidth"),
            _ when track.StartsWith("Component.", StringComparison.Ordinal) => app?.FindResource(track),
            _ => null,
        };
        return value switch
        {
            double size => new GridLength(size),
            GridLength length => length,
            _ => GridLength.Parse(track),
        };
    }

    private static object[] Rows(ListBox list) =>
        [.. ((System.Collections.IEnumerable)(list.ItemsSource ?? list.Items)).Cast<object>().Where(item => item is not IDenseTableGroupHeading)];

    private static void OnGotFocus(object? sender, RoutedEventArgs args)
    {
        if (sender is not ListBox list || !ReferenceEquals(args.Source, list)) return;
        var rows = Rows(list);
        if (rows.Length == 0) return;
        var target = list.SelectedItem is { } selected && rows.Contains(selected) ? selected : rows[0];
        list.SelectedItem = target;
        FocusRow(list, target);
    }

    [KeyboardShortcutHandler("Table:PreviousRow", "Table:NextRow", "Table:NextAction", "Table:PreviousAction",
        "Table:OpenActionsMenu", "Table:OpenDetails", "Table:FirstItem", "Table:LastItem")]
    private static void OnKeyDown(object? sender, KeyEventArgs args)
    {
        if (sender is not ListBox list) return;
        var binding = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Table, args.Key, args.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.PreviousRow, KeyboardShortcutBehavior.NextRow,
                KeyboardShortcutBehavior.NextAction, KeyboardShortcutBehavior.PreviousAction,
                KeyboardShortcutBehavior.OpenActionsMenu, KeyboardShortcutBehavior.OpenDetails,
                KeyboardShortcutBehavior.FirstItem, KeyboardShortcutBehavior.LastItem]);
        if (binding is null || !KeyboardShortcutRegistry.Allows(binding,
                KeyboardShortcutRegistry.IsTextInput(args.Source),
                list.SelectedItem is not null || args.Source is ListBox or ListBoxItem))
            return;
        if (MoveTab(list, binding.Behavior, args.KeyModifiers))
        {
            args.Handled = true;
            return;
        }
        if (args.Source is Visual visual && visual.GetSelfAndVisualAncestors().OfType<ListBoxItem>()
                .FirstOrDefault()?.DataContext is IDenseTableGroupHeading)
            return;
        if (binding.Behavior == KeyboardShortcutBehavior.OpenDetails)
        {
            if (args.Source is ListBox or ListBoxItem && list.SelectedItem is { } row &&
                list.GetValue(OpenDetailsProperty) is { } open)
            {
                open(row);
                args.Handled = true;
            }
            return;
        }
        if (binding.Behavior == KeyboardShortcutBehavior.OpenActionsMenu)
        {
            if (list.SelectedItem is { } current && list.ContainerFromItem(current) is { } container &&
                container.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains(MenuClass))
                    is { Flyout: { } flyout } menu)
            {
                flyout.ShowAt(menu);
                args.Handled = true;
            }
            return;
        }
        if (binding.Behavior is not (KeyboardShortcutBehavior.PreviousRow or KeyboardShortcutBehavior.NextRow or
                KeyboardShortcutBehavior.FirstItem or KeyboardShortcutBehavior.LastItem) ||
            args.Source is not ListBox and not ListBoxItem)
            return;
        var rows = Rows(list);
        if (rows.Length == 0) return;
        var index = Array.IndexOf(rows, list.SelectedItem);
        var forward = binding.Behavior == KeyboardShortcutBehavior.NextRow;
        var next = binding.Behavior switch
        {
            KeyboardShortcutBehavior.FirstItem => 0,
            KeyboardShortcutBehavior.LastItem => rows.Length - 1,
            _ => index < 0 ? forward ? 0 : rows.Length - 1 : index + (forward ? 1 : -1),
        };
        if ((uint)next < (uint)rows.Length)
        {
            list.SelectedItem = rows[next];
            FocusRow(list, rows[next]);
        }
        args.Handled = true;
    }

    private static void FocusRow(ListBox list, object item)
    {
        list.ScrollIntoView(item);
        if (list.ContainerFromItem(item) is { } container)
        {
            container.Focus(NavigationMethod.Directional);
            return;
        }

        Dispatcher.UIThread.Post(() => list.ContainerFromItem(item)?.Focus(NavigationMethod.Directional),
            DispatcherPriority.Loaded);
    }
}
