using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The current Selection's word list.</summary>
public sealed partial class WordsPage : UserControl
{
    /// <summary>Creates the page over the workspace's shared page model.</summary>
    public WordsPage(WorkspaceContext context) : this(
        (context ?? throw new ArgumentNullException(nameof(context))).GetOrCreatePage(static owner => new WordsPageModel(owner))) { }

    public WordsPage(WordsPageModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        AvaloniaXamlLoader.Load(this);
        this.FindControl<ContentControl>("WordsHost")!.Content = new TextWordsPanel(model.Words);
    }
}
