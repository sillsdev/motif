using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The What changed view compares two Assessments and lists words that moved between cells.</summary>
public sealed partial class DifferencePanel : UserControl
{
    public DifferencePanel(DifferenceViewModel difference)
    {
        ArgumentNullException.ThrowIfNull(difference);
        Difference = difference;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        var words = this.FindControl<ListBox>("DifferencePanelWordsItems")!;
        WordPresentationHost = new WordListPresentationHost<MovedWordViewModel>(
            words, row => row.Presentation, WordListCardSections.Moved);
    }

    public DifferenceViewModel Difference { get; }

    /// <summary>The list-owned word-card and navigation host for What changed.</summary>
    public IWordPresentationHost WordPresentationHost { get; }

    /// <summary>Converts a component size to the type required by a grid column.</summary>
    public static readonly IValueConverter SizeToGridLength = new FuncValueConverter<double, GridLength>(
        size => new GridLength(size));
}
