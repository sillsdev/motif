using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Results stage's In text view, bound to its own view model.</summary>
public sealed partial class ResultsInTextPanel : UserControl
{
    public ResultsInTextPanel(ResultsInTextViewModel inText)
    {
        ArgumentNullException.ThrowIfNull(inText);
        InText = inText;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public ResultsInTextViewModel InText { get; }

    private void OnGoToTextsClick(object? sender, RoutedEventArgs e) => InText.OpenTexts?.Invoke();

    // A press on a link in the block is the link's own; a press elsewhere on it opens the word's comparison.
    private void OnTokenPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<HyperlinkButton>(includeSelf: true) is not null) return;
        if (sender is Control { Tag: ResultsTokenViewModel token } block)
        {
            block.Focus();
            InText.SelectToken(token);
        }
    }

    private void OnTokenKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Control { Tag: ResultsTokenViewModel token }) return;
        InText.SelectToken(token);
        e.Handled = true;
    }
}
