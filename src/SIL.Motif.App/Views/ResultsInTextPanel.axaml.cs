using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.Contract.Requests;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Controls.WordPresentation;

namespace SIL.Motif.App.Views;

/// <summary>The Analyze texts view, bound to its own view model.</summary>
public sealed partial class ResultsInTextPanel : UserControl
{
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
        ScrollEndAnchor.Attach(_textScrollViewer);
        WordHost = new ResultsTextWordPresentationHost(InText, this);
        var readerToolbar = this.FindControl<Grid>("ReaderToolbar")!;
        var filterChips = this.FindControl<WrapPanel>("ResultsInTextFilterChips")!;
        readerToolbar.LayoutUpdated += (_, _) => SetFilterChipRow(readerToolbar, filterChips);
        SetFilterChipRow(readerToolbar, filterChips);
        _ = new VisibleControlLifetime(this, () => { InText.ShowReader(); DataContext = this; }, () =>
        {
            DataContext = null;
            InText.CloseTokenCard();
            InText.HideReader();
        });
        InText.OccurrenceRequested += async anchor => await FocusOccurrenceAsync(anchor).ConfigureAwait(true);
        InText.OccurrenceRealizationRequested += BringNativeOccurrenceAsync;
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

    public IWordPresentationHost WordHost { get; }

    internal void ReturnFocusToWord(ResultsTokenViewModel token)
    {
        if (token.Occurrence is { } anchor) _ = FocusOccurrenceAsync(anchor);
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

    internal int ScreenLineCount(ResultsTokenViewModel token)
    {
        var viewport = _lineItems.FindAncestorOfType<ScrollViewer>()?.Viewport.Height ?? _lineItems.Bounds.Height;
        var lineIndex = NativeLineIndex(token);
        var realized = lineIndex < 0 ? 0 : _lineItems.ContainerFromIndex(lineIndex)?.Bounds.Height ?? 0;
        return Math.Max(1, (int)Math.Floor(viewport / Math.Max(1, realized)));
    }

    // The card is built under its line after the selection changes, so focus waits for that layout pass.
    private async void FocusCard(ResultsTokenViewModel token)
    {
        if (token.Occurrence is { } anchor)
            await BringNativeOccurrenceAsync(anchor).ConfigureAwait(true);
        if (!ReferenceEquals(InText.SelectedToken, token)) return;
        var card = this.GetVisualDescendants().OfType<WordCard>().FirstOrDefault(card =>
            card.Key == token.PresentationKey);
        if (card is null) return;
        card.FocusCard();
        card.BringIntoView();
    }

    private int NativeLineIndex(ResultsTokenViewModel token) => token.Occurrence is { } anchor ?
        InText.VisibleHeaders.ToList().FindIndex(header => header.ParagraphId == anchor.ParagraphId &&
            header.SegmentId == anchor.SegmentId) : -1;

    internal async Task<bool> FocusNativeNeighbourAsync(ResultsTokenViewModel token, int direction)
    {
        var anchor = token.Occurrence;
        if (anchor is null) return false;
        var next = InText.AdjacentVisibleOccurrence(anchor, direction);
        return next is not null && await FocusOccurrenceAsync(next).ConfigureAwait(true);
    }

    internal Task<bool> FocusLineAsync(ResultsTokenViewModel token, int offset, bool clamp = false)
    {
        var index = NativeLineIndex(token);
        if (index < 0 || InText.VisibleHeaders.Count == 0) return Task.FromResult(false);
        var target = index + offset;
        if (clamp) target = Math.Clamp(target, 0, InText.VisibleHeaders.Count - 1);
        return target < 0 || target >= InText.VisibleHeaders.Count ? Task.FromResult(false) : FocusBoundaryAsync(target);
    }

    internal Task<bool> FocusBoundaryAsync(int index)
    {
        if (InText.VisibleHeaders.Count == 0) return Task.FromResult(false);
        var header = InText.VisibleHeaders[Math.Clamp(index, 0, InText.VisibleHeaders.Count - 1)];
        var anchor = InText.FirstOccurrenceInLine(header);
        return anchor is null ? Task.FromResult(false) : FocusOccurrenceAsync(anchor);
    }

    internal async Task<bool> FocusOccurrenceAsync(OccurrenceAnchor anchor)
    {
        await InText.SelectionRefresh.ConfigureAwait(true);
        var focused = false;
        for (var pass = 0; pass < 4; pass++)
        {
            var token = await BringNativeOccurrenceAsync(anchor).ConfigureAwait(true);
            if (token is null || InText.LinePages is not { } pages) return false;
            if (!ReferenceEquals(pages, InText.LinePages) || !IsEffectivelyVisible) return false;
            var strip = StripFor(token);
            if (strip is null) continue;
            focused = strip.Focus(NavigationMethod.Directional);
            strip.BringIntoView();
            await Dispatcher.UIThread.InvokeAsync(_lineItems.UpdateLayout, DispatcherPriority.Loaded);
            if (!ReferenceEquals(pages, InText.LinePages) || !IsEffectivelyVisible) return false;
            var shown = StripFor(token);
            var origin = shown?.TranslatePoint(default, _textScrollViewer);
            if (focused && shown?.IsFocused == true && origin is { } point &&
                new Rect(_textScrollViewer.Viewport).Intersects(new Rect(point, shown.Bounds.Size))) return true;
        }
        return focused;
    }

    private async Task<ResultsTokenViewModel?> BringNativeOccurrenceAsync(OccurrenceAnchor anchor)
    {
        if (!IsEffectivelyVisible || !InText.SelectOccurrenceText(anchor, resetFilters: false)) return null;
        var lineIndex = InText.VisibleHeaders.ToList().FindIndex(header => header.ParagraphId == anchor.ParagraphId &&
            header.SegmentId == anchor.SegmentId);
        if (lineIndex < 0 || InText.LinePages is not { } pages) return null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            _lineItems.ScrollIntoView(lineIndex);
            if (lineIndex == 0) _textScrollViewer.ScrollToHome();
            else if (lineIndex == InText.VisibleHeaders.Count - 1) _textScrollViewer.ScrollToEnd();
            _lineItems.UpdateLayout();
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Loaded);
            if (!ReferenceEquals(pages, InText.LinePages)) return null;
            var line = pages.RealizedLines.FirstOrDefault(line => line.TextId == anchor.TextId &&
                line.ParagraphId == anchor.ParagraphId && line.SegmentId == anchor.SegmentId);
            var words = this.GetVisualDescendants().OfType<ProgressiveItemsControl>().FirstOrDefault(control =>
                line is not null && ReferenceEquals(control.FullItemsSource, line.DisplayTokens));
            if (words is null) continue;
            var sourceOffset = InText.SourceTokenOffset(anchor);
            await words.ShowSourceIndexAsync(sourceOffset).ConfigureAwait(true);
            await Dispatcher.UIThread.InvokeAsync(_lineItems.UpdateLayout, DispatcherPriority.Loaded);
            if (!ReferenceEquals(pages, InText.LinePages) || !IsEffectivelyVisible) return null;
            var arrived = await InText.ReadOccurrenceAsync(anchor, resetFilters: false, realizeViewport: false).ConfigureAwait(true);
            if (!ReferenceEquals(pages, InText.LinePages) || !IsEffectivelyVisible) return null;
            if (arrived is not null && StripFor(arrived) is not null) return arrived;
        }
        return null;
    }

    internal static ResultsTokenViewModel? TokenOf(Control control) => control is WordStripToken strip ? strip.Data : control.Tag switch
    {
        SelectionTokenPosition position => position.Model,
        ResultsTokenViewModel token => token,
        _ => null,
    };

    private WordStripToken? StripFor(ResultsTokenViewModel token) => this.GetVisualDescendants().OfType<WordStripToken>()
        .FirstOrDefault(strip => ReferenceEquals(strip.Data, token));

    /// <summary>Opens a word card for Enter or Space and leaves other keys available to the control.</summary>
    /// <param name="key">The key pressed on the word.</param>
    /// <param name="token">The word occurrence attached to the focused control.</param>
    /// <param name="openTokenCard">The action that selects and opens the occurrence's card.</param>
    /// <returns>Whether the key opened the card.</returns>
    internal static async Task<bool> OpenTokenCardOnKeyboardAsync(Key key, ResultsTokenViewModel? token,
        Func<ResultsTokenViewModel, Task> openTokenCard)
    {
        ArgumentNullException.ThrowIfNull(openTokenCard);
        if (key is not (Key.Enter or Key.Space) || token is null) return false;
        await openTokenCard(token).ConfigureAwait(true);
        return true;
    }
}
