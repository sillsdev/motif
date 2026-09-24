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
    public static readonly Avalonia.StyledProperty<string> HintProperty =
        Avalonia.AvaloniaProperty.Register<MiniMatrix, string>(nameof(Hint), "Filter by the matrix (click to add or remove a cell)");

    public static readonly Avalonia.StyledProperty<bool> IsInteractiveProperty =
        Avalonia.AvaloniaProperty.Register<MiniMatrix, bool>(nameof(IsInteractive), true);

    public MiniMatrix() => AvaloniaXamlLoader.Load(this);

    /// <summary>The line above the matrix, saying what it is for here.</summary>
    public string Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>False where the matrix only shows cells another view chose, so a click must not change them.</summary>
    public bool IsInteractive
    {
        get => GetValue(IsInteractiveProperty);
        set => SetValue(IsInteractiveProperty, value);
    }

    // Beside a list, a click adds or removes one cell: narrowing by several cells is the usual move there.
    private void OnCellPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsInteractive || DataContext is not CompareViewModel compare || sender is not Control { Tag: CompareCellViewModel cell } control) return;
        if (cell.IsEmptyImpossible) return;
        control.Focus();
        compare.Toggle(cell, additive: true);
        e.Handled = true;
    }

    private void OnCellKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsInteractive || e.Key is not (Key.Enter or Key.Space) || DataContext is not CompareViewModel compare) return;
        if (sender is not Control { Tag: CompareCellViewModel cell }) return;
        compare.Toggle(cell, additive: true);
        e.Handled = true;
    }
}
