using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Compare matrix in Texts: what the project held against what the parser built.</summary>
public sealed partial class ComparePanel : UserControl
{
    private const double ExpandedMatrixLabelWidth = 1000;

    public ComparePanel(CompareViewModel compare)
    {
        ArgumentNullException.ThrowIfNull(compare);
        Compare = compare;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, e) => SetExpandedMatrixLabel(e.NewSize.Width >= ExpandedMatrixLabelWidth);
        SetExpandedMatrixLabel(Bounds.Width >= ExpandedMatrixLabelWidth);
    }

    public CompareViewModel Compare { get; }

    private void SetExpandedMatrixLabel(bool expanded)
    {
        var isExpanded = Classes.Contains("wideMatrix");
        if (expanded == isExpanded) return;
        if (expanded) Classes.Add("wideMatrix");
        else Classes.Remove("wideMatrix");
    }

    /// <summary>The words a sort choice is shown with.</summary>
    public static readonly IValueConverter SortLabel = new FuncValueConverter<CompareSort, string>(sort => sort switch
    {
        CompareSort.Alphabetical => "A to Z",
        CompareSort.Slowest => "Slowest first",
        _ => "Most places first",
    });

    // Ctrl or Shift adds a cell to the choice, as in any list; a plain click chooses it alone.
    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: CompareCellViewModel cell } control || cell.IsEmptyImpossible) return;
        control.Focus();
        Compare.Toggle(cell, additive: e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }

    private void OnCellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            sender is not Control { Tag: CompareCellViewModel { IsEmptyImpossible: false } cell }) return;
        Compare.Toggle(cell, additive: e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }
}
