using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The checked Texts' word list, project status, last Assessment, analyses and places.</summary>
public sealed partial class TextWordsPanel : UserControl
{
    public TextWordsPanel(TextWordsViewModel words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Words = words;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public TextWordsViewModel Words { get; }
}
