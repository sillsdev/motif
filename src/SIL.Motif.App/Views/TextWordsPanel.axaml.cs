using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The checked Texts' word list, project status, last Assessment, analyses and places.</summary>
public sealed partial class TextWordsPanel : UserControl
{
    /// <summary>Limits the wrapped toolbar to half the page height so the virtualized word rows retain a viewport.</summary>
    public static readonly FuncValueConverter<double, double> ToolbarHeightLimit = new(height => height / 2);

    public TextWordsPanel(TextWordsViewModel words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Words = words;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        PresentationHost = new TextWordPresentationHost(words, this);
        _ = new VisibleControlLifetime(this, () =>
        {
            Words.ShowVisibleRows();
            DataContext = this;
        }, () =>
        {
            DataContext = null;
            Words.HideVisibleRows();
        });
        this.FindControl<ListBox>("TextWordsPanelRowsItems")!.ContainerPrepared += (_, args) =>
        {
            if (args.Container.DataContext is SelectionWordRowPosition { Model: { } row }) Words.RealizeRow(row);
        };
        this.FindControl<ListBox>("TextWordsPanelRowsItems")!.ContainerClearing += (_, args) =>
        {
            if (args.Container.DataContext is SelectionWordRowPosition { CapturedModel: { } row }) Words.ReleaseVisibleRow(row);
        };
    }

    public TextWordsViewModel Words { get; }

    /// <summary>The list adapter for its word rows, evidence and virtualized navigation.</summary>
    public IWordPresentationHost PresentationHost { get; }
}
