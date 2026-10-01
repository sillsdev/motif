using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// One word as every word list shows it: tick box, word and gloss, the opinion and FieldWorks' morphemes,
/// PanGloss's outcome, the meaning, warnings, places, time and read state, then the three next steps, always in that
/// order. A list may hide the tick box but never moves a column. Clicking the word, or Enter on the focused row,
/// opens the list's card inside the row; Esc closes it, and Up and Down move between rows.
/// </summary>
/// <remarks>
/// The row's facts come from <see cref="Row"/>; what belongs to the list hosting it (the tick, whether the card is
/// open, the card itself, a staged change and a note) comes through the other properties, so one control serves
/// every list. Its columns line up with a <see cref="WordRowHeader"/> inside the same shared-size scope.
/// </remarks>
public sealed partial class WordRow : UserControl
{
    public static readonly StyledProperty<WordRowViewModel?> RowProperty =
        AvaloniaProperty.Register<WordRow, WordRowViewModel?>(nameof(Row));

    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<WordRow, bool>(nameof(IsOpen), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool?> IsCheckedProperty =
        AvaloniaProperty.Register<WordRow, bool?>(nameof(IsChecked), false, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> ShowsTickProperty =
        AvaloniaProperty.Register<WordRow, bool>(nameof(ShowsTick), true);

    public static readonly StyledProperty<bool> ShowsMeaningProperty =
        AvaloniaProperty.Register<WordRow, bool>(nameof(ShowsMeaning), true);

    public static readonly StyledProperty<object?> CardProperty =
        AvaloniaProperty.Register<WordRow, object?>(nameof(Card));

    public static readonly StyledProperty<string?> StagedTextProperty =
        AvaloniaProperty.Register<WordRow, string?>(nameof(StagedText));

    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<WordRow, string?>(nameof(Note));

    public static readonly StyledProperty<string> ListProperty =
        AvaloniaProperty.Register<WordRow, string>(nameof(List), "words");

    public static readonly StyledProperty<ICommand?> OpenedCommandProperty =
        AvaloniaProperty.Register<WordRow, ICommand?>(nameof(OpenedCommand));

    public static readonly StyledProperty<object?> OpenedCommandParameterProperty =
        AvaloniaProperty.Register<WordRow, object?>(nameof(OpenedCommandParameter));

    public static readonly StyledProperty<WordRowColumns> ColumnsProperty =
        AvaloniaProperty.Register<WordRow, WordRowColumns>(nameof(Columns), WordRowColumns.All);

    public static readonly StyledProperty<string?> TimeTextProperty =
        AvaloniaProperty.Register<WordRow, string?>(nameof(TimeText));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<WordRow, object?>(nameof(Actions));

    public WordRow()
    {
        AvaloniaXamlLoader.Load(this);
        _root = this.FindControl<Border>("Root")!;
        _body = this.FindControl<Border>("Body")!;
        // A local null keeps the row's cells from reading the host's item before a row is given.
        _root.DataContext = null;
        _body.AddHandler(PointerPressedEvent, OnBodyPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        _body.AddHandler(PointerReleasedEvent, OnBodyReleased, RoutingStrategies.Bubble, handledEventsToo: true);
        _body.KeyDown += OnBodyKeyDown;
        AddHandler(KeyDownEvent, OnRowKeyDown, handledEventsToo: false);
        _layout = new WordRowLayout(this);
        _layout.Apply(Columns, ShowsMeaning);
    }

    private readonly WordRowLayout? _layout;

    private const double ClickSlop = 4;
    private readonly Border _root;
    private Point? _pressedAt;
    private readonly Border _body;

    /// <summary>The word's facts and next steps, the same for the word on every page.</summary>
    public WordRowViewModel? Row
    {
        get => GetValue(RowProperty);
        set => SetValue(RowProperty, value);
    }

    /// <summary>Whether the card shows inside the row.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    /// <summary>Whether the word is ticked, for the list's actions on ticked words.</summary>
    public bool? IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>Whether the list offers a tick box; a list without one leaves the column empty, never removes it.</summary>
    public bool ShowsTick
    {
        get => GetValue(ShowsTickProperty);
        set => SetValue(ShowsTickProperty, value);
    }

    /// <summary>
    /// Whether this list's meaning column shows now: a list whose words share one meaning hides it, with its head. It
    /// hides only a column <see cref="Columns"/> lets the page show, and the column then takes no width or gap.
    /// </summary>
    public bool ShowsMeaning
    {
        get => GetValue(ShowsMeaningProperty);
        set => SetValue(ShowsMeaningProperty, value);
    }

    /// <summary>What the row shows inside itself when open: the list's word card.</summary>
    public object? Card
    {
        get => GetValue(CardProperty);
        set => SetValue(CardProperty, value);
    }

    /// <summary>A change waiting for Apply, in the window's words, under the word; empty for none.</summary>
    public string? StagedText
    {
        get => GetValue(StagedTextProperty);
        set => SetValue(StagedTextProperty, value);
    }

    /// <summary>One line the list adds under the row, such as why the word is in Fix these first.</summary>
    public string? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    /// <summary>The list's name in the row's automation ids, such as <c>matrix</c>, <c>fix-first</c> or <c>lists</c>.</summary>
    public string List
    {
        get => GetValue(ListProperty);
        set => SetValue(ListProperty, value);
    }

    /// <summary>Run when a click or a key opens the card, so a list can follow the word elsewhere too.</summary>
    public ICommand? OpenedCommand
    {
        get => GetValue(OpenedCommandProperty);
        set => SetValue(OpenedCommandProperty, value);
    }

    public object? OpenedCommandParameter
    {
        get => GetValue(OpenedCommandParameterProperty);
        set => SetValue(OpenedCommandParameterProperty, value);
    }

    /// <summary>
    /// The columns this list shows. A hidden column takes no width, and the rest keep their order; the word and the
    /// three next steps always show. A list's <see cref="WordRowHeader"/> takes the same value.
    /// </summary>
    public WordRowColumns Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>
    /// The list's own measure of the word's time, such as a rule's share of it, in place of the parse time the
    /// row carries; <see langword="null"/> shows the row's. The column widens to hold it.
    /// </summary>
    public string? TimeText
    {
        get => GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    /// <summary>
    /// The list's own actions on this word, such as Undo, on a line under the row beside its note. They are not next
    /// steps, so they never take a column; a click on one leaves the card as it is.
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>Gives the row itself keyboard focus, with the focus ring, as Tab would.</summary>
    public void FocusRow() => _body.Focus(NavigationMethod.Tab);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RowProperty)
        {
            _root.DataContext = Row;
            SetIds();
        }
        else if (change.Property == ListProperty)
        {
            SetIds();
        }
        else if (change.Property == ColumnsProperty || change.Property == ShowsMeaningProperty)
        {
            _layout?.Apply(Columns, ShowsMeaning);
        }
    }

    private void SetIds()
    {
        if (Row is not { } row) return;
        string Id(string part) => row.AutomationIdOf(List, part);
        AutomationProperties.SetAutomationId(_body, Id("row"));
        AutomationProperties.SetAutomationId(this.FindControl<CheckBox>("Tick")!, Id("tick"));
        AutomationProperties.SetAutomationId(this.FindControl<HyperlinkButton>("OpenInText")!, Id("open-in-text"));
        AutomationProperties.SetAutomationId(this.FindControl<HyperlinkButton>("TryWord")!, Id("try-a-word"));
        AutomationProperties.SetAutomationId(this.FindControl<HyperlinkButton>("WordAnalyses")!, Id("word-analyses"));
        AutomationProperties.SetAutomationId(this.FindControl<Border>("CardHost")!, Id("card"));
    }

    private void Toggle()
    {
        IsOpen = !IsOpen;
        if (IsOpen && OpenedCommand is { } opened && opened.CanExecute(OpenedCommandParameter))
            opened.Execute(OpenedCommandParameter);
    }

    // A plain click opens the card; a drag or double-click selects the row's words instead, so they stay copyable.
    private void OnBodyPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(_body);
        var onControl = e.Source is Visual source && source.GetSelfAndVisualAncestors()
            .TakeWhile(visual => !ReferenceEquals(visual, _body)).Any(visual => visual is Button or ToggleButton);
        _pressedAt = point.Properties.IsLeftButtonPressed && e.ClickCount == 1 && !onControl ? point.Position : null;
    }

    private void OnBodyReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedAt is not { } start) return;
        _pressedAt = null;
        var moved = e.GetPosition(_body) - start;
        if (Math.Abs(moved.X) > ClickSlop || Math.Abs(moved.Y) > ClickSlop) return;
        if (e.Source is SelectableTextBlock { SelectedText.Length: > 0 }) return;
        _body.Focus(NavigationMethod.Pointer);
        Toggle();
    }

    private void OnBodyKeyDown(object? sender, KeyEventArgs e)
    {
        // The tick and the next steps inside the row keep their own keys.
        if (!ReferenceEquals(e.Source, _body)) return;
        switch (e.Key)
        {
            case Key.Enter or Key.Space:
                Toggle();
                break;
            case Key.Down:
                MoveFocus(1);
                break;
            case Key.Up:
                MoveFocus(-1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Esc anywhere in an open row, its card or its links included, closes the card and returns to the row.
    private void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !IsOpen) return;
        IsOpen = false;
        FocusRow();
        e.Handled = true;
    }

    private void MoveFocus(int step)
    {
        if (this.FindAncestorOfType<ItemsControl>() is not { } list) return;
        var container = this.GetVisualAncestors().OfType<Control>().FirstOrDefault(ancestor =>
            list.IndexFromContainer(ancestor) >= 0);
        if (container is null) return;
        var next = list.IndexFromContainer(container) + step;
        if (next < 0 || next >= list.ItemCount) return;
        list.ScrollIntoView(next);
        list.ContainerFromIndex(next)?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault()?.FocusRow();
    }
}

/// <summary>
/// The word row's columns a list can hide. The word and the three next steps are not here: every row shows them.
/// <see cref="Morphemes"/> shows the morphemes beside the opinion and the outcome; without it each column shows only
/// its mark, and the outcome says its word.
/// </summary>
[Flags]
public enum WordRowColumns
{
    None = 0,
    Tick = 1,
    FieldWorks = 2,
    PanGloss = 4,
    Morphemes = 8,
    Meaning = 16,
    Warnings = 32,
    Places = 64,
    Time = 128,
    Read = 256,
    All = Tick | FieldWorks | PanGloss | Morphemes | Meaning | Warnings | Places | Time | Read,
}

/// <summary>
/// Shows only the chosen columns of a word row or its header: a hidden column's definition leaves its grid, so it
/// takes neither width nor a column gap, and the columns left keep their order and their shared-size groups.
/// </summary>
internal sealed class WordRowLayout
{
    private readonly Control _owner;
    private readonly Grid _line;
    private readonly Grid _cells;
    private readonly (ColumnDefinition Column, Control Cell)[] _lineParts;
    private readonly (ColumnDefinition Column, Control Cell)[] _cellParts;

    public WordRowLayout(Control owner)
    {
        _owner = owner;
        _line = Find<Grid>("Line");
        _cells = Find<Grid>("Cells");
        _lineParts = [.. _line.ColumnDefinitions.Zip([Find<Control>("TickCell"), _cells, Find<Control>("NextCell")])];
        _cellParts = [.. _cells.ColumnDefinitions.Zip(new[]
        {
            "WordCell", "FieldWorksCell", "PanGlossCell", "MeaningCell", "WarningsCell", "PlacesCell", "TimeCell", "ReadCell",
        }.Select(Find<Control>))];
        _sharedWidths = [_cellParts[1].Column.Width, _cellParts[2].Column.Width];
    }

    // The widths the FieldWorks and PanGloss columns share the free width with, as the markup gives them.
    private readonly GridLength[] _sharedWidths;

    public void Apply(WordRowColumns columns, bool showsMeaning)
    {
        bool Shows(WordRowColumns column) => (columns & column) == column;
        var morphemes = Shows(WordRowColumns.Morphemes);
        // Morphemes share the free width; marks alone size to the widest in the list, as the other columns do.
        SetWidth(_cellParts[1].Column, morphemes, _sharedWidths[0], "WordRowFieldWorks");
        SetWidth(_cellParts[2].Column, morphemes, _sharedWidths[1], "WordRowPanGloss");
        foreach (var name in new[] { "FieldWorksMorphemes", "PanGlossMorphemes", "OutcomeBesideMorphemes" })
            SetShown(name, morphemes);
        SetShown("OutcomeAlone", !morphemes);
        Show(_line, _lineParts, [Shows(WordRowColumns.Tick), true, true]);
        Show(_cells, _cellParts,
        [
            true, Shows(WordRowColumns.FieldWorks), Shows(WordRowColumns.PanGloss), Shows(WordRowColumns.Meaning) && showsMeaning,
            Shows(WordRowColumns.Warnings), Shows(WordRowColumns.Places), Shows(WordRowColumns.Time),
            Shows(WordRowColumns.Read),
        ]);
    }

    private static void SetWidth(ColumnDefinition column, bool shares, GridLength sharedWidth, string group)
    {
        column.Width = shares ? sharedWidth : GridLength.Auto;
        column.SharedSizeGroup = shares ? null : group;
    }

    private static void Show(Grid grid, (ColumnDefinition Column, Control Cell)[] parts, bool[] shown)
    {
        grid.ColumnDefinitions.Clear();
        foreach (var ((column, cell), visible) in parts.Zip(shown))
        {
            cell.IsVisible = visible;
            if (!visible) continue;
            Grid.SetColumn(cell, grid.ColumnDefinitions.Count);
            grid.ColumnDefinitions.Add(column);
        }
    }

    // The header has no morphemes or outcome marks, so those parts are optional.
    private void SetShown(string name, bool shown)
    {
        if (_owner.FindControl<Control>(name) is { } part) part.IsVisible = shown;
    }

    private T Find<T>(string name) where T : Control =>
        _owner.FindControl<T>(name) ?? throw new InvalidOperationException($"The word row has no part named {name}.");
}
