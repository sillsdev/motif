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
    private const double ShortMatrixHeight = 720;

    public ComparePanel(CompareViewModel compare)
    {
        ArgumentNullException.ThrowIfNull(compare);
        Compare = compare;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        SizeChanged += (_, e) =>
        {
            SetExpandedMatrixLabel(e.NewSize.Width >= ExpandedMatrixLabelWidth);
            SetShortMatrix(e.NewSize.Height);
        };
        SetExpandedMatrixLabel(Bounds.Width >= ExpandedMatrixLabelWidth);
        SetShortMatrix(Bounds.Height);
    }

    public CompareViewModel Compare { get; }
    internal double ShortMatrixCheckedHeight { get; private set; }

    private void SetExpandedMatrixLabel(bool expanded)
    {
        var isExpanded = Classes.Contains("wideMatrix");
        if (expanded == isExpanded) return;
        if (expanded) Classes.Add("wideMatrix");
        else Classes.Remove("wideMatrix");
    }

    private void SetShortMatrix(double panelHeight)
    {
        ShortMatrixCheckedHeight = panelHeight;
        var shortMatrix = panelHeight > 0 && panelHeight < ShortMatrixHeight;
        var isShort = Classes.Contains("shortMatrix");
        if (shortMatrix == isShort) return;
        if (shortMatrix) Classes.Add("shortMatrix");
        else Classes.Remove("shortMatrix");
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

    [KeyboardShortcutHandler("Matrix:ToggleMatrixCell")]
    private void OnCellKeyDown(object? sender, KeyEventArgs e)
    {
        var entry = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.Matrix, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.ToggleMatrixCell]);
        if (entry?.Behavior != KeyboardShortcutBehavior.ToggleMatrixCell ||
            sender is not Control { Tag: CompareCellViewModel { IsEmptyImpossible: false } cell } ||
            !KeyboardShortcutRegistry.Allows(entry, KeyboardShortcutRegistry.IsTextInput(e.Source), hasFocusedItem: true)) return;
        Compare.Toggle(cell, additive: e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        e.Handled = true;
    }
}
