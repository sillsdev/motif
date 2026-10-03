using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    private void OnGoToTextsClick(object? sender, RoutedEventArgs e) => InText.OpenTexts?.Invoke();

    private void OnAncestorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == IsVisibleProperty && !IsEffectivelyVisible) InText.CloseTokenCard();
    }

    private async void OnTokenPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control { Tag: ResultsTokenViewModel token } form &&
            form.FindAncestorOfType<Border>() is { } strip)
        {
            strip.Focus();
            await InText.OpenTokenCardAsync(token);
        }
    }

    private async void OnTokenKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { Tag: ResultsTokenViewModel token } strip) return;
        if (e.Key is Key.Left or Key.Right)
        {
            e.Handled = FocusNeighbour(strip, e.Key == Key.Left ? -1 : 1);
            return;
        }
        if (await OpenTokenCardOnKeyboardAsync(e.Key, token, InText.OpenTokenCardAsync)) e.Handled = true;
    }

    private bool FocusNeighbour(Control strip, int direction)
    {
        if (strip.Tag is not ResultsTokenViewModel current) return false;
        var tokens = InText.VisibleLines.SelectMany(line => line.Tokens).Where(token => token.IsWord).ToList();
        var index = tokens.IndexOf(current);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= tokens.Count) return false;
        BringTokenLineIntoView(tokens[next]);
        return StripFor(tokens[next])?.Focus(NavigationMethod.Directional) == true;
    }

    private async void OnTokenCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control card || e.Source is Control source && !ReferenceEquals(source, card)) return;
        if (e.Key == Key.Escape)
        {
            CloseCardOntoItsWord();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Left or Key.Right)) return;
        e.Handled = true;
        await InText.MoveTokenCardAsync(e.Key == Key.Left ? -1 : 1).ConfigureAwait(true);
    }

    private void OnCloseCardClick(object? sender, RoutedEventArgs e) => CloseCardOntoItsWord();

    private void CloseCardOntoItsWord()
    {
        if (InText.SelectedToken is not { } token) return;
        InText.CloseTokenCard();
        Dispatcher.UIThread.Post(() =>
        {
            BringTokenLineIntoView(token);
            StripFor(token)?.Focus(NavigationMethod.Directional);
        }, DispatcherPriority.Loaded);
    }

    // The card is built under its line after the selection changes, so focus waits for that layout pass.
    private void FocusCard(ResultsTokenViewModel token)
    {
        if (!ReferenceEquals(InText.SelectedToken, token)) return;
        BringTokenLineIntoView(token);
        var card = this.GetVisualDescendants().OfType<Border>().FirstOrDefault(border =>
            border.Classes.Contains("wordCard") && ReferenceEquals(border.DataContext, token));
        if (card is null) return;
        card.Focus(NavigationMethod.Directional);
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

    private Border? StripFor(ResultsTokenViewModel token) => this.GetVisualDescendants().OfType<Border>()
        .FirstOrDefault(border => border.Name == "WordStrip" && ReferenceEquals(border.Tag, token));

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
