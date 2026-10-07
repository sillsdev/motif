using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

public sealed partial class TextsListsPanel : UserControl
{
    public TextsListsPanel(TextsListsViewModel lists)
    {
        ArgumentNullException.ThrowIfNull(lists);
        Lists = lists;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        PresentationHost = new CompareWordPresentationHost(lists.Compare,
            this.FindControl<ListBox>("TextsListsPanelWordsItems")!, WordListOwner.Lists);
    }

    public TextsListsViewModel Lists { get; }

    public IWordPresentationHost PresentationHost { get; }
}
