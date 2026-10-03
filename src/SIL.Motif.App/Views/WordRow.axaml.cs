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

    static WordRow()
    {
        RowProperty.Changed.AddClassHandler<WordRow>((row, _) => row.ApplyLayout());
        ColumnsProperty.Changed.AddClassHandler<WordRow>((row, _) => row.ApplyLayout());
        ShowsMeaningProperty.Changed.AddClassHandler<WordRow>((row, _) => row.ApplyLayout());
    }

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
        ApplyLayout();
    }

    private readonly WordRowLayout? _layout;

    private const double ClickSlop = 4;
    private readonly Border _root;
    private Point? _pressedAt;
    private readonly Border _body;

    private void ApplyLayout() => _layout?.Apply(Columns, ShowsMeaning);

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

    [KeyboardShortcutHandler(
        "WordList:OpenWordCard", "Lists:OpenWordCard", "WordList:PreviousRow", "Lists:PreviousRow",
        "WordList:NextRow", "Lists:NextRow", "Matrix:OpenWordCard", "Matrix:PreviousRow", "Matrix:NextRow",
        "ReviewChanges:OpenWordCard", "ReviewChanges:PreviousRow", "ReviewChanges:NextRow")]
    private void OnBodyKeyDown(object? sender, KeyEventArgs e)
    {
        // The tick and the next steps inside the row keep their own keys.
        if (!ReferenceEquals(e.Source, _body)) return;
        var scope = WordShortcutScope();
        var entry = scope is { } wordScope
            ? KeyboardShortcutRegistry.Find(wordScope, e.Key, e.KeyModifiers, targetBehaviors:
                [KeyboardShortcutBehavior.OpenWordCard, KeyboardShortcutBehavior.PreviousRow,
                    KeyboardShortcutBehavior.NextRow])
            : null;
        if (entry is not null && !KeyboardShortcutRegistry.Allows(entry,
                KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true)) return;
        switch (entry?.Behavior)
        {
            case KeyboardShortcutBehavior.OpenWordCard:
                Toggle();
                break;
            case KeyboardShortcutBehavior.NextRow:
                MoveFocus(1);
                break;
            case KeyboardShortcutBehavior.PreviousRow:
                MoveFocus(-1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // Esc anywhere in an open row, its card or its links included, closes the card and returns to the row.
    [KeyboardShortcutHandler(
        "WordList:CloseWordCard", "Lists:CloseWordCard", "WordList:PreviousScreen", "Lists:PreviousScreen",
        "WordList:NextScreen", "Lists:NextScreen", "WordList:FirstItem", "Lists:FirstItem",
        "WordList:LastItem", "Lists:LastItem", "WordList:Approve", "Lists:Approve",
        "WordList:Disapprove", "Lists:Disapprove", "WordList:Unknown", "Lists:Unknown",
        "Matrix:CloseWordCard", "Matrix:PreviousScreen", "Matrix:NextScreen", "Matrix:FirstItem", "Matrix:LastItem",
        "ReviewChanges:CloseWordCard", "ReviewChanges:PreviousScreen", "ReviewChanges:NextScreen",
        "ReviewChanges:FirstItem", "ReviewChanges:LastItem")]
    private async void OnRowKeyDown(object? sender, KeyEventArgs e)
    {
        var wordScope = WordShortcutScope();
        var entry = wordScope is { } scope
            ? KeyboardShortcutRegistry.Find(scope, e.Key, e.KeyModifiers, targetBehaviors:
                [KeyboardShortcutBehavior.CloseWordCard, KeyboardShortcutBehavior.PreviousScreen,
                    KeyboardShortcutBehavior.NextScreen, KeyboardShortcutBehavior.FirstItem,
                    KeyboardShortcutBehavior.LastItem, KeyboardShortcutBehavior.Approve,
                    KeyboardShortcutBehavior.Disapprove, KeyboardShortcutBehavior.Unknown])
            : null;
        if (entry is null || IsNestedAction(e.Source)
            || !KeyboardShortcutRegistry.Allows(entry,
                KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true)) return;
        switch (entry.Behavior)
        {
            case KeyboardShortcutBehavior.CloseWordCard when IsOpen:
                IsOpen = false;
                FocusRow();
                e.Handled = true;
                break;
            case KeyboardShortcutBehavior.PreviousScreen:
            case KeyboardShortcutBehavior.NextScreen:
                e.Handled = MovePage(entry.Behavior == KeyboardShortcutBehavior.PreviousScreen ? -1 : 1);
                break;
            case KeyboardShortcutBehavior.FirstItem:
            case KeyboardShortcutBehavior.LastItem:
                e.Handled = MoveBoundary(entry.Behavior == KeyboardShortcutBehavior.FirstItem);
                break;
            case KeyboardShortcutBehavior.Approve:
            case KeyboardShortcutBehavior.Disapprove:
            case KeyboardShortcutBehavior.Unknown:
                e.Handled = true;
                var result = await StageOpinionAsync(entry.Behavior).ConfigureAwait(true);
                KeyboardShortcutStatus.Announce(this, result.StatusMessage);
                break;
        }
    }

    private bool MovePage(int direction)
    {
        var list = this.FindAncestorOfType<ItemsControl>();
        var index = RowIndex(list);
        if (list is null || index < 0) return false;
        var viewportHeight = list.FindAncestorOfType<ScrollViewer>()?.Viewport.Height ?? list.Bounds.Height;
        var visibleRows = Math.Max(1, (int)Math.Floor(viewportHeight / Math.Max(1, Bounds.Height)));
        return FocusAt(list, Math.Clamp(index + direction * visibleRows, 0, list.ItemCount - 1));
    }

    private KeyboardShortcutScope? WordShortcutScope() => List switch
    {
        "word-list" => KeyboardShortcutScope.WordList,
        "lists" => KeyboardShortcutScope.Lists,
        "matrix" => KeyboardShortcutScope.Matrix,
        "review" => KeyboardShortcutScope.ReviewChanges,
        _ => null,
    };

    private bool MoveBoundary(bool first)
    {
        var list = this.FindAncestorOfType<ItemsControl>();
        return list is not null && list.ItemCount > 0 && FocusAt(list, first ? 0 : list.ItemCount - 1);
    }

    private bool FocusAt(ItemsControl list, int index)
    {
        if (index < 0 || index >= list.ItemCount) return false;
        list.ScrollIntoView(index);
        list.UpdateLayout();
        var container = list.ContainerFromIndex(index);
        var row = container is WordRow direct ? direct : container?.GetVisualDescendants().OfType<WordRow>().FirstOrDefault();
        row?.FocusRow();
        return row is not null;
    }

    private int RowIndex(ItemsControl? list)
    {
        if (list is null) return -1;
        var container = this.GetVisualAncestors().OfType<Control>().FirstOrDefault(ancestor => list.IndexFromContainer(ancestor) >= 0);
        return container is null ? -1 : list.IndexFromContainer(container);
    }

    private async Task<KeyboardOpinionShortcutResult> StageOpinionAsync(KeyboardShortcutBehavior behavior)
    {
        if (DataContext is TextWordRowViewModel word) return await word.StageOpinionShortcutAsync(behavior);
        if (DataContext is CompareWordViewModel compare) return await compare.StageOpinionShortcutAsync(behavior);
        var name = Row?.Word ?? "This word";
        return new(true, $"{name} has no stored analysis to change.");
    }

    private bool IsNestedAction(object? source) => source is Control control &&
        control.GetSelfAndVisualAncestors().TakeWhile(ancestor => !ReferenceEquals(ancestor, this))
            .Any(ancestor => ancestor is Button or ToggleButton or CheckBox or TextBox or ComboBox);

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
/// <see cref="FieldWorksMorphemes"/> and <see cref="PanGlossMorphemes"/> show the morphemes beside the opinion and
/// the outcome; without them the column shows only its mark, and the outcome says its word.
/// </summary>
[Flags]
public enum WordRowColumns
{
    None = 0,
    Tick = 1,
    FieldWorks = 2,
    PanGloss = 4,
    FieldWorksMorphemes = 8,
    PanGlossMorphemes = 16,
    Meaning = 32,
    Warnings = 64,
    Places = 128,
    Time = 256,
    Read = 512,
    All = Tick | FieldWorks | FieldWorksMorphemes | PanGloss | PanGlossMorphemes | Meaning | Warnings | Places | Time |
        Read,
}

/// <summary>
/// The columns each page beyond the Matrix and Lists shows, chosen for what its reader asks there. Whatever a page
/// hides is still in the card the row opens, and every row keeps the word and the three next steps.
/// </summary>
public static class WordRowColumnSets
{
    /// <summary>
    /// Timing asks which words cost the time and whether that time bought an answer: the opinion, the outcome and the
    /// time Timing measured. Morphemes, the meaning, places and read state are in the card.
    /// </summary>
    public static readonly WordRowColumns Timing = WordRowColumns.FieldWorks | WordRowColumns.PanGloss | WordRowColumns.Time;

    /// <summary>
    /// The Overview names its slowest words only as a way in, inside the Speed tile: the word and its time. The rest is
    /// on Timing and in the card.
    /// </summary>
    public static readonly WordRowColumns Overview = WordRowColumns.Time;

    /// <summary>
    /// Review changes asks what Apply will write: what FieldWorks holds now and what PanGloss built, with their
    /// morphemes; the staged arrow leads the line under the row. Meaning, places, time and read state do not change
    /// what is written.
    /// </summary>
    public static readonly WordRowColumns Review = WordRowColumns.FieldWorks | WordRowColumns.FieldWorksMorphemes |
        WordRowColumns.PanGloss | WordRowColumns.PanGlossMorphemes;

    /// <summary>
    /// What changed lists the words of one chosen move, which already names the opinion and meaning they left and
    /// reached: the row adds the outcome now and how many places the word has, and its note gives the earlier answer.
    /// </summary>
    public static readonly WordRowColumns WhatChanged = WordRowColumns.PanGloss | WordRowColumns.Places;

    /// <summary>
    /// Warnings asks how each related word fares and where it occurs. It shows the FieldWorks mark, PanGloss outcome
    /// and places; resolved parser morphemes were not requested for these rows, as their typed availability records.
    /// </summary>
    public static readonly WordRowColumns Warnings = WordRowColumns.FieldWorks | WordRowColumns.PanGloss |
        WordRowColumns.Places;

    /// <summary>
    /// Analyze texts' Word list asks what FieldWorks holds for each word of the Selection, ticked for AI Handoff: the
    /// opinion and FieldWorks' morphemes, PanGloss's outcome, places and read state. The meaning is the Matrix's
    /// question and the time Timing's, and both are in the card or a click away; warnings join once they name words.
    /// </summary>
    public static readonly WordRowColumns WordList = WordRowColumns.Tick | WordRowColumns.FieldWorks |
        WordRowColumns.FieldWorksMorphemes | WordRowColumns.PanGloss | WordRowColumns.Meaning |
        WordRowColumns.Places | WordRowColumns.Time | WordRowColumns.Read;
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
        _cellParts[1].Column.Bind(ColumnDefinition.MinWidthProperty,
            _cellParts[1].Cell.GetObservable(Control.MinWidthProperty));
        _cellParts[2].Column.Bind(ColumnDefinition.MinWidthProperty,
            _cellParts[2].Cell.GetObservable(Control.MinWidthProperty));
    }

    // The widths the FieldWorks and PanGloss columns share the free width with, as the markup gives them.
    private readonly GridLength[] _sharedWidths;

    public void Apply(WordRowColumns columns, bool showsMeaning)
    {
        bool Shows(WordRowColumns column) => (columns & column) == column;
        var fieldWorks = Shows(WordRowColumns.FieldWorksMorphemes);
        var panGloss = Shows(WordRowColumns.PanGlossMorphemes);
        _line.Classes.Set("wordList", columns == WordRowColumnSets.WordList);
        _cells.Classes.Set("marksOnly", !panGloss);
        _cells.Classes.Set("fieldWorksMorphemesOnly", fieldWorks && !panGloss);
        // Morphemes share the free width; marks alone size to the widest in the list, as the other columns do.
        SetWidth(_cellParts[1].Column, fieldWorks, _sharedWidths[0], "WordRowFieldWorks");
        SetWidth(_cellParts[2].Column, panGloss, _sharedWidths[1], "WordRowPanGloss");
        SetShown("FieldWorksMorphemes", fieldWorks);
        SetShown("PanGlossMorphemes", panGloss);
        SetShown("OutcomeBesideMorphemes", panGloss);
        SetShown("OutcomeAlone", !panGloss);
        var row = (_owner as WordRow)?.Row;
        if (_owner.FindControl<Control>("WordGloss") is { } wordGloss)
            wordGloss.IsVisible = !string.IsNullOrWhiteSpace(row?.Gloss);
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
