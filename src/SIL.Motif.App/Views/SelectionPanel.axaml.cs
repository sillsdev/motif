using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Text and word Selection editor, and the checked Texts' words, bound to their own view models.</summary>
public sealed partial class SelectionPanel : UserControl
{
    public SelectionPanel(SelectionViewModel selection, TextWordsViewModel words)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(words);
        Selection = selection;
        Words = words;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public SelectionViewModel Selection { get; }

    public TextWordsViewModel Words { get; }

    // A press on a link in the block is the link's own; a press elsewhere on it opens the word's details.
    private void OnReaderTokenPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<HyperlinkButton>(includeSelf: true) is not null) return;
        if (sender is Control { Tag: ReaderTokenViewModel token } block)
        {
            block.Focus();
            Words.SelectToken(token);
        }
    }

    private void OnReaderTokenKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || sender is not Control { Tag: ReaderTokenViewModel token }) return;
        Words.SelectToken(token);
        e.Handled = true;
    }
}
