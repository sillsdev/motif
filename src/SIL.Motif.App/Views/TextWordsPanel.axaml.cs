using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
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
        PresentationHost = new TextWordPresentationHost(words, this);
    }

    public TextWordsViewModel Words { get; }

    /// <summary>The list adapter for its word rows, evidence and virtualized navigation.</summary>
    public IWordPresentationHost PresentationHost { get; }
}
