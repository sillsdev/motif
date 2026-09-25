using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
    }

    public TextsListsViewModel Lists { get; }
}
