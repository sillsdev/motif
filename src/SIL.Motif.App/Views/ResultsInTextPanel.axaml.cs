using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Analyze texts view, bound to its own view model.</summary>
public sealed partial class ResultsInTextPanel : UserControl
{
    private readonly List<Control> _visibilityAncestors = [];
    private readonly ItemsControl _lineItems;
    private readonly ScrollViewer _textScrollViewer;

    public ResultsInTextPanel(ResultsInTextViewModel inText)
    {
        ArgumentNullException.ThrowIfNull(inText);
        InText = inText;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        _lineItems = this.FindControl<ItemsControl>("TextLineItems")!;
        _textScrollViewer = this.FindControl<ScrollViewer>("TextScrollViewer")!;
        WordHost = new ResultsTextWordPresentationHost(InText, this);
        var readerToolbar = this.FindControl<Grid>("ReaderToolbar")!;
        var filterChips = this.FindControl<WrapPanel>("ResultsInTextFilterChips")!;
        readerToolbar.LayoutUpdated += (_, _) => SetFilterChipRow(readerToolbar, filterChips);
        SetFilterChipRow(readerToolbar, filterChips);
        InText.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ResultsInTextViewModel.SelectedToken) && InText.SelectedToken is { } token)
                Dispatcher.UIThread.Post(() => FocusCard(token), DispatcherPriority.Loaded);
            else if (e.PropertyName == nameof(ResultsInTextViewModel.SelectedText))
            {
                var selectedText = InText.SelectedText;
                Dispatcher.UIThread.Post(() =>
                {
                    if (ReferenceEquals(selectedText, InText.SelectedText)) _textScrollViewer.Offset = Vector.Zero;
                }, DispatcherPriority.Loaded);
            }
        };
    }

    public ResultsInTextViewModel InText { get; }

    /// <summary>The occurrence host used by Analyze word strips and cards.</summary>
    public IWordPresentationHost WordHost { get; }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors().OfType<Control>())
        {
            _visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnAncestorPropertyChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        InText.CloseTokenCard();
        foreach (var ancestor in _visibilityAncestors)
            ancestor.PropertyChanged -= OnAncestorPropertyChanged;
        _visibilityAncestors.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    internal bool FocusNeighbour(ResultsTokenViewModel current, int direction)
    {
        var tokens = InText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToArray();
        var index = Array.IndexOf(tokens, current);
        var next = index + direction;
        return index >= 0 && next >= 0 && next < tokens.Length && FocusOccurrence(tokens[next]);
    }

    internal bool FocusLine(ResultsTokenViewModel current, int offset, bool clamp = false)
    {
        var currentLine = InText.VisibleLines.FirstOrDefault(line => line.Tokens.Contains(current));
        var index = currentLine is null ? -1 : InText.VisibleLines.IndexOf(currentLine);
        var target = index + offset;
        if (index < 0) return false;
        if (clamp) target = Math.Clamp(target, 0, InText.VisibleLines.Count - 1);
        return target >= 0 && target < InText.VisibleLines.Count && FocusBoundary(target);
    }

    internal bool FocusBoundary(int index)
    {
        if (InText.VisibleLines.Count == 0) return false;
        var targetIndex = Math.Clamp(index, 0, InText.VisibleLines.Count - 1);
        var token = InText.VisibleLines[targetIndex].Tokens.FirstOrDefault(item => item.IsWord);
        return token is not null && FocusOccurrence(token);
    }

    internal int ScreenLineCount(ResultsTokenViewModel token)
    {
        var viewport = _lineItems.FindAncestorOfType<ScrollViewer>()?.Viewport.Height ?? _lineItems.Bounds.Height;
        var line = InText.VisibleLines.FirstOrDefault(candidate => candidate.Tokens.Contains(token));
        var index = line is null ? -1 : InText.VisibleLines.IndexOf(line);
        var realized = index < 0 ? 0 : _lineItems.ContainerFromIndex(index)?.Bounds.Height ?? 0;
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, realized)));
    }

    internal void ReturnFocusToWord(ResultsTokenViewModel token)
    {
        Dispatcher.UIThread.Post(() => FocusOccurrence(token), DispatcherPriority.Loaded);
    }

    private static void SetFilterChipRow(Grid toolbar, WrapPanel filterChips)
    {
        var inlineWidth = toolbar.ColumnDefinitions[2].ActualWidth;
        var requiredWidth = filterChips.Children.OfType<Control>().Where(chip => chip.IsVisible)
            .Sum(chip => chip.DesiredSize.Width);
        var row = inlineWidth > 0 && requiredWidth <= inlineWidth ? 0 : 1;
        var column = row == 1 ? 0 : 2;
        var columnSpan = row == 1 ? 5 : 1;
        if (Grid.GetRow(filterChips) == row && Grid.GetColumn(filterChips) == column &&
            Grid.GetColumnSpan(filterChips) == columnSpan) return;
        Grid.SetRow(filterChips, row);
        Grid.SetColumn(filterChips, column);
        Grid.SetColumnSpan(filterChips, columnSpan);
    }

    private void OnGoToTextsClick(object? sender, RoutedEventArgs e) => InText.OpenTexts?.Invoke();

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty && !IsEffectivelyVisible) InText.CloseTokenCard();
    }

    private bool FocusOccurrence(ResultsTokenViewModel token)
    {
        BringTokenLineIntoView(token);
        var strip = this.GetVisualDescendants().OfType<WordStripToken>()
            .FirstOrDefault(candidate => ReferenceEquals(candidate.Data, token));
        if (strip is null) return false;
        strip.FocusWord();
        return true;
    }

    private void FocusCard(ResultsTokenViewModel token)
    {
        if (!ReferenceEquals(InText.SelectedToken, token)) return;
        BringTokenLineIntoView(token);
        var card = this.GetVisualDescendants().OfType<WordCard>()
            .FirstOrDefault(candidate => candidate.Key == token.PresentationKey);
        if (card is null) return;
        card.FocusCard();
        card.BringIntoView();
    }

    private void BringTokenLineIntoView(ResultsTokenViewModel token)
    {
        var line = InText.VisibleLines.FirstOrDefault(line => line.Tokens.Contains(token));
        if (line is null) return;
        _lineItems.ScrollIntoView(InText.VisibleLines.IndexOf(line));
        _lineItems.UpdateLayout();
        foreach (var words in this.GetVisualDescendants().OfType<ProgressiveItemsControl>())
            if (ReferenceEquals(words.FullItemsSource, line.Tokens)) words.ShowItem(token);
        _lineItems.UpdateLayout();
    }
}
