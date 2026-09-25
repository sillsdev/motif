using Avalonia.Controls;
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
}
