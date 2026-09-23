using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Results stage's Compare view: the matrix of what the project held against what the parser did.</summary>
public sealed partial class ComparePanel : UserControl
{
    public ComparePanel(CompareViewModel compare)
    {
        ArgumentNullException.ThrowIfNull(compare);
        Compare = compare;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public CompareViewModel Compare { get; }

    /// <summary>The words a sort choice is shown with.</summary>
    public static readonly IValueConverter SortLabel = new FuncValueConverter<CompareSort, string>(sort => sort switch
    {
        CompareSort.Alphabetical => "A to Z",
        CompareSort.Slowest => "Slowest first",
        _ => "Most frequent first",
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
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Control { Tag: CompareCellViewModel cell }) return;
        Compare.Toggle(cell, additive: e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }
}
