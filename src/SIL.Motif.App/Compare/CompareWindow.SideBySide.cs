using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.TextEditing;
using SIL.TextEditing.Avalonia;
using TextDocument = SIL.TextEditing.Document;

namespace SIL.Motif.App.Compare;

public sealed partial class CompareWindow
{
    private const double SideBySideWidth = 1600;
    private readonly Dictionary<int, RichTextField> _right = [];
    private readonly Dictionary<int, Control> _left = [];
    private readonly Dictionary<int, Stack<IReadOnlyList<ParagraphVersion>?>> _undo = [];
    private readonly Dictionary<int, Button> _undoButtons = [];
    private Control? _sideBySide;
    private double _combinedWidth;

    /// <summary>Whether the side-by-side view is showing.</summary>
    public bool IsSideBySide => _sideBySide is not null && ReferenceEquals(((Grid)Content!).Children[0], _sideBySide);

    /// <summary>The editable field on the right for each sorted paragraph, while the side-by-side view shows.</summary>
    public IReadOnlyDictionary<int, RichTextField> ResultFields => _right;

    /// <summary>
    /// Shows the side-by-side view in a larger window: FieldWorks read-only on the left, the person's version on the
    /// right, starting as their text with the choices made so far, row for row.
    /// </summary>
    public void OpenSideBySide()
    {
        if (IsSideBySide) return;
        _combinedWidth = Width;
        Width = Math.Max(Width, SideBySideWidth);
        _sideBySide = BuildSideBySide();
        Swap(_sideBySide);
        UpdateFooter();
        _right.Values.FirstOrDefault()?.Focus();
    }

    /// <summary>Returns to the combined view with every choice, and every paragraph typed on the right, kept.</summary>
    public void BackToCombinedView()
    {
        if (!IsSideBySide) return;
        TakeTypedRows();
        _sideBySide = null;
        _right.Clear();
        _left.Clear();
        _undoButtons.Clear();
        Width = _combinedWidth > 0 ? _combinedWidth : Width;
        Swap(_combined);
        Render();
        FocusFirst();
    }

    /// <summary>Copies sorted paragraph <paramref name="paragraph"/>'s FieldWorks version over the right side; Undo puts it back.</summary>
    public void UseThis(int paragraph)
    {
        var sorted = _comparison.Sorted.Paragraphs[paragraph];
        if (!_undo.TryGetValue(paragraph, out var stack)) _undo[paragraph] = stack = new Stack<IReadOnlyList<ParagraphVersion>?>();
        stack.Push(_comparison.RewrittenAt(paragraph));
        _comparison.Rewrite(paragraph, sorted.FieldWorks is null ? [] : [sorted.FieldWorks]);
        Refill(paragraph);
    }

    /// <summary>Undoes the last "Use this" on <paramref name="paragraph"/>; false when there is none.</summary>
    public bool UndoUseThis(int paragraph)
    {
        if (!_undo.TryGetValue(paragraph, out var stack) || stack.Count == 0) return false;
        _comparison.Rewrite(paragraph, stack.Pop());
        Refill(paragraph);
        return true;
    }

    private void Swap(Control body)
    {
        var frame = (Grid)Content!;
        frame.Children.RemoveAt(0);
        frame.Children.Insert(0, body);
    }

    private Control BuildSideBySide()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(4) };
        var lockedHeading = "\U0001F512 FieldWorks, read-only" + (_savedAt is null ? "" : $" (saved {_savedAt})");
        AddRow(grid, 0, new TextBlock { Text = lockedHeading, FontWeight = FontWeight.Bold },
            new TextBlock(), new TextBlock { Text = "Yours, editable: this becomes the result", FontWeight = FontWeight.Bold });
        foreach (var paragraph in _comparison.Sorted.Paragraphs)
        {
            var row = paragraph.Index + 1;
            var left = Left(paragraph);
            var right = RightField(paragraph.Index);
            var gutter = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8, 0) };
            var useThis = new Button { Content = ChangeWords.UseThis, Tag = paragraph.Index };
            AutomationProperties.SetName(useThis, $"Use the FieldWorks version of paragraph {paragraph.Index + 1}");
            useThis.Click += (_, _) => UseThis(paragraph.Index);
            var undo = new Button { Content = "Undo", Tag = paragraph.Index, IsVisible = false };
            undo.Click += (_, _) => UndoUseThis(paragraph.Index);
            _undoButtons[paragraph.Index] = undo;
            gutter.Children.Add(useThis);
            gutter.Children.Add(undo);
            _left[paragraph.Index] = left;
            AddRow(grid, row, left, gutter, new Border { BorderBrush = MineBrush, BorderThickness = new Thickness(1), Child = right, Tag = paragraph.Index });
        }
        var scroll = new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var back = new Button { Content = ChangeWords.BackToCombined };
        back.Click += (_, _) => BackToCombinedView();
        var top = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        top.Children.Add(new TextBlock { Text = Title, FontWeight = FontWeight.Bold, FontSize = 16 });
        top.Children.Add(new TextBlock
        {
            Text = "The right side becomes the result. Use this → copies a FieldWorks paragraph across; anything typed on the right counts as yours.",
            TextWrapping = TextWrapping.Wrap,
        });
        top.Children.Add(back);
        var view = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, Dock.Top);
        view.Children.Add(top);
        view.Children.Add(scroll);
        return view;
    }

    private static void AddRow(Grid grid, int row, Control left, Control gutter, Control right)
    {
        while (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        foreach (var (control, column) in new[] { (left, 0), (gutter, 1), (right, 2) })
        {
            control.Margin = new Thickness(control.Margin.Left, 4, control.Margin.Right, 4);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, column);
            grid.Children.Add(control);
        }
    }

    private Control Left(SortedParagraph paragraph)
    {
        if (paragraph.FieldWorks is not { } version)
        {
            var gap = new TextBlock { Text = "Not in FieldWorks", FontStyle = FontStyle.Italic, Foreground = Brushes.DimGray, Focusable = true, Tag = paragraph.Index };
            AutomationProperties.SetName(gap, $"FieldWorks, read-only, paragraph {paragraph.Index + 1}: not in FieldWorks");
            return gap;
        }
        var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, FlowDirection = Direction(version), Tag = paragraph.Index };
        foreach (var run in version.Runs) block.Inlines!.Add(Styled(run));
        AutomationProperties.SetName(block, $"FieldWorks, read-only, paragraph {paragraph.Index + 1}");
        return block;
    }

    private RichTextField RightField(int index)
    {
        var sorted = _comparison.Sorted.Paragraphs[index];
        var versions = _comparison.RewrittenAt(index) ?? (sorted.Whole is { } whole &&
            (_comparison.ChoiceFor(whole) == Choice.FieldWorks ? sorted.FieldWorks : sorted.Mine) is null ? [] : [_comparison.Paragraph(sorted)]);
        var document = new TextDocument();
        foreach (var version in versions) document.Paragraphs.Add(CompareDocuments.ToParagraph(version));
        if (document.Paragraphs.Count == 0)
            document.Paragraphs.Add(new Paragraph { DefaultWs = (sorted.Mine ?? sorted.FieldWorks)?.Runs.FirstOrDefault()?.Ws ?? "en" });
        var field = new RichTextField(document) { MinHeight = 28, Tag = index };
        if (_writingSystems is not null) field.WritingSystems = _writingSystems;
        if (_styles is not null) field.StyleResolver = _styles;
        AutomationProperties.SetName(field, $"Yours, editable, the result, paragraph {index + 1}");
        field.Editor.Committed += _ => Typed(index, field);
        _right[index] = field;
        return field;
    }

    // Typing on the right makes that row's paragraphs the person's, in place of the choices inside it.
    private void Typed(int index, RichTextField field)
    {
        if (!_right.TryGetValue(index, out var current) || current != field) return;
        _comparison.Rewrite(index, Paragraphs(field));
    }

    private static IReadOnlyList<ParagraphVersion> Paragraphs(RichTextField field)
    {
        var paragraphs = field.Editor.Document.Paragraphs;
        // One empty paragraph is a row with no paragraph at all.
        if (paragraphs.Count == 1 && paragraphs[0].Length == 0 && paragraphs[0].Key is null) return [];
        return paragraphs.Select(CompareDocuments.FromParagraph).ToArray();
    }

    private void Refill(int index)
    {
        if (!_right.TryGetValue(index, out var old) || old.Parent is not Border holder) return;
        holder.Child = null;
        holder.Child = RightField(index);
        if (_undoButtons.TryGetValue(index, out var undo)) undo.IsVisible = _undo.TryGetValue(index, out var stack) && stack.Count > 0;
    }

    private void TakeTypedRows()
    {
        foreach (var (index, field) in _right)
            if (_comparison.RewrittenAt(index) is not null) _comparison.Rewrite(index, Paragraphs(field));
    }

    // Done in side by side takes every row on the right as written, so nothing waits for a choice.
    private void TakeRightSide()
    {
        foreach (var (index, field) in _right) _comparison.Rewrite(index, Paragraphs(field));
    }

    private int? CurrentRow()
    {
        for (var at = FocusManager?.GetFocusedElement() as Visual; at is not null; at = at.GetVisualParent())
            if (at is Control { Tag: int row }) return row;
        return null;
    }

    private void SwitchSide()
    {
        var row = CurrentRow() ?? 0;
        var onRight = _right.TryGetValue(row, out var field) && (FocusManager?.GetFocusedElement() as Visual)?.FindAncestorOfType<RichTextField>(includeSelf: true) == field;
        if (onRight && _left.TryGetValue(row, out var left))
        {
            left.Focusable = true;
            left.Focus(NavigationMethod.Directional);
        }
        else if (field is not null) field.Focus(NavigationMethod.Directional);
    }
}
