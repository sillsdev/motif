using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Text and word Selection editor, bound to its own <see cref="Selection"/> view model.</summary>
public sealed partial class SelectionPanel : UserControl
{
    public SelectionPanel(SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        Selection = selection;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public SelectionViewModel Selection { get; }
}
