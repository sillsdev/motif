using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Grammar page's findings about the grammar as a whole, bound to <see cref="Grammar"/>.</summary>
public sealed partial class GrammarPanel : UserControl
{
    public GrammarPanel(GrammarViewModel grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        Grammar = grammar;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        Grammar.Warnings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GrammarWarningsViewModel.ShownCount) or
                nameof(GrammarWarningsViewModel.Bucket) or nameof(GrammarWarningsViewModel.SelectedGroup))
                ShowColumnsThatSaySomething();
        };
        ShowColumnsThatSaySomething();
    }

    public GrammarViewModel Grammar { get; }

    // A column holding the same word on every row says nothing, so it steps aside for the meaning.
    private void ShowColumnsThatSaySomething()
    {
        var columns = this.FindControl<DataGrid>("FindingsGrid")!.Columns;
        if (columns.FirstOrDefault(column => (string?)column.Header == "Where") is { } where)
            where.IsVisible = Grammar.Warnings.AnyShownWhere;
        if (columns.FirstOrDefault(column => (string?)column.Header == "Level") is { } level)
            level.IsVisible = Grammar.Warnings.AnyShownLevelsDiffer;
    }
}
