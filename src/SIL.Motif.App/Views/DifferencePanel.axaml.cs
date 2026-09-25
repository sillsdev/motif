using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
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

    /// <summary>Converts a component size to the type required by a grid column.</summary>
    public static readonly IValueConverter SizeToGridLength = new FuncValueConverter<double, GridLength>(
        size => new GridLength(size));
}
