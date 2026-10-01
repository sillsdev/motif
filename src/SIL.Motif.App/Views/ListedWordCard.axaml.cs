using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SIL.Motif.App.Views;

/// <summary>
/// The card a word row opens on a page that lists words beside its own content: the word's card from the latest
/// parse, the same as in the Matrix and Lists, or a line saying the parse has not reached the word.
/// </summary>
public sealed partial class ListedWordCard : UserControl
{
    public ListedWordCard() => AvaloniaXamlLoader.Load(this);
}
