using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// A small copy of the Compare matrix that filters another list by the same chosen cells; its data context is the
/// <see cref="CompareViewModel"/> it mirrors.
/// </summary>
public sealed partial class MiniMatrix : UserControl
{
    public MiniMatrix() => AvaloniaXamlLoader.Load(this);

    // Beside a list, a click adds or removes one cell: narrowing by several cells is the usual move there.
    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not CompareViewModel compare || sender is not Control { Tag: CompareCellViewModel cell } control) return;
        if (cell.IsEmptyImpossible) return;
        control.Focus();
        compare.Toggle(cell, additive: true);
        e.Handled = true;
    }

    private void OnCellKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || DataContext is not CompareViewModel compare) return;
        if (sender is not Control { Tag: CompareCellViewModel cell }) return;
        compare.Toggle(cell, additive: true);
        e.Handled = true;
    }
}
