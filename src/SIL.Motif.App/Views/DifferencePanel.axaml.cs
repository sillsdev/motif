using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Results stage's "What changed" view: two runs of the same words, and the words that moved between cells.</summary>
public sealed partial class DifferencePanel : UserControl
{
    public DifferencePanel(DifferenceViewModel difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        Difference = difference;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public DifferenceViewModel Difference { get; }
}
