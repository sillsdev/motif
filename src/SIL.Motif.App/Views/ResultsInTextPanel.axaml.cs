using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Analyze texts view, bound to its own view model.</summary>
public sealed partial class ResultsInTextPanel : UserControl
{
    private readonly List<Control> _visibilityAncestors = [];

    public ResultsInTextPanel(ResultsInTextViewModel inText)
    {
        ArgumentNullException.ThrowIfNull(inText);
        InText = inText;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
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

    // A press on a link in the block is the link's own; a press elsewhere on it opens the word's comparison.
    private async void OnTokenPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<HyperlinkButton>(includeSelf: true) is not null) return;
        if (sender is Control { Tag: ResultsTokenViewModel token } block)
        {
            block.Focus();
            await InText.OpenTokenCardAsync(token);
        }
    }

    private async void OnTokenKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not Control { Tag: ResultsTokenViewModel token }) return;
        if (await OpenTokenCardOnKeyboardAsync(e.Key, token, InText.OpenTokenCardAsync)) e.Handled = true;
    }

    private async void OnTokenCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            InText.CloseTokenCard();
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Left or Key.Right)) return;
        await InText.MoveTokenCardAsync(e.Key == Key.Left ? -1 : 1).ConfigureAwait(true);
        e.Handled = true;
    }

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
