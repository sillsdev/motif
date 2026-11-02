using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

using SIL.Motif.App.Services;
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
        FixFirstPresentationHost = new CompareWordPresentationHost(compare,
            this.FindControl<ListBox>("ComparePanelFixFirstRowsItems")!, WordListOwner.FixFirst);
        MatrixPresentationHost = new CompareWordPresentationHost(compare,
            this.FindControl<ListBox>("ComparePanelWordsItems")!, WordListOwner.Matrix);
        _ = new VisibleControlLifetime(this, () => Compare.ShowVisibleRows(this),
            () => Compare.ReleaseVisibleRows(this));
        SizeChanged += (_, e) =>
        {
            SetExpandedMatrixLabel(e.NewSize.Width >= ExpandedMatrixLabelWidth);
            SetShortMatrix(e.NewSize.Height);
        };
        LayoutUpdated += (_, _) =>
        {
            ApplyShortMatrixToCells();
            RestoreKeptScroll();
        };
        SetExpandedMatrixLabel(Bounds.Width >= ExpandedMatrixLabelWidth);
        SetShortMatrix(Bounds.Height);
        AddHandler(GotFocusEvent, (_, e) => _focusWasInTodayWords = InTodayWords(e.Source as Visual), handledEventsToo: true);
        AttachedToVisualTree += (_, _) => Compare.PropertyChanged += OnCompareChanged;
        DetachedFromVisualTree += (_, _) => Compare.PropertyChanged -= OnCompareChanged;
    }

    private const int KeptScrollLayouts = 64;
    private bool _focusWasInTodayWords;
    private ScrollViewer? _watchedScroll;
    private Vector? _keptOffset;
    private int _keptLayouts;

    private bool InTodayWords(Visual? visual) => visual is not null && new[] { "TodayWordsChips", "FixFirstCard", "ChosenWordsCard" }
        .Any(name => this.FindControl<Control>(name) is { } part && (part == visual || part.IsVisualAncestorOf(visual)));

    private void OnCompareChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CompareViewModel.Words) or nameof(CompareViewModel.ShowsFixFirst) or
            nameof(CompareViewModel.ShowsTodayWords) or nameof(CompareViewModel.WordListSchema)) KeepScrollThroughNewWords();
        if (e.PropertyName is nameof(CompareViewModel.ShowsTodayWords) or nameof(CompareViewModel.ShowsWordList) &&
            _focusWasInTodayWords && (!Compare.ShowsTodayWords || !Compare.ShowsWordList))
            Avalonia.Threading.Dispatcher.UIThread.Post(KeepFocusOnTheMatrix);
    }

    // A new list or columns empty the card for a layout pass or two, which would pull the page up.
    private void KeepScrollThroughNewWords()
    {
        if (this.FindControl<ScrollViewer>("MatrixScroll") is not { } scroll) return;
        if (_keptOffset is null)
        {
            _keptOffset = scroll.Offset;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _keptOffset = null, Avalonia.Threading.DispatcherPriority.Background);
        }
        _keptLayouts = 0;
        if (_watchedScroll == scroll) return;
        _watchedScroll = scroll;
        scroll.PropertyChanged += (_, args) =>
        {
            if (args.Property == ScrollViewer.ExtentProperty) RestoreKeptScroll();
        };
    }

    private void RestoreKeptScroll()
    {
        if (_keptOffset is not { } offset || this.FindControl<ScrollViewer>("MatrixScroll") is not { } scroll) return;
        if (scroll.Offset != offset) scroll.Offset = offset;
        if (++_keptLayouts >= KeptScrollLayouts) _keptOffset = null;
    }

    // Words or chips that hide with the Proposal's view take focus with them; it moves to a cell, not to nothing.
    private void KeepFocusOnTheMatrix()
    {
        _focusWasInTodayWords = false;
        var focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() as Visual;
        if (focused is not null && focused.IsEffectivelyVisible) return;
        var cells = this.GetVisualDescendants().OfType<MatrixCell>().Where(cell =>
            cell.IsEffectivelyVisible && cell.Tag is CompareCellViewModel { IsEmptyImpossible: false }).ToList();
        (cells.FirstOrDefault(cell => cell.Tag == Compare.ChosenCell) ?? cells.FirstOrDefault())?.Focus();
    }

    public CompareViewModel Compare { get; }
    public IWordPresentationHost FixFirstPresentationHost { get; }
    public IWordPresentationHost MatrixPresentationHost { get; }
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
        ApplyShortMatrixToCells();
    }

    private void ApplyShortMatrixToCells()
    {
        using var span = MotifTrace.Begin("compare.short-matrix-walk");
        MotifTrace.Count("compare-layout-updated");
        var isShort = Classes.Contains("shortMatrix");
        foreach (var cell in this.GetVisualDescendants().OfType<MatrixCell>())
            if (cell.ShowsDetails == isShort)
                cell.ShowsDetails = !isShort;
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
