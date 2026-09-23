using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Grammar stage: the parser's findings about the grammar as a whole, bound to <see cref="Grammar"/>.</summary>
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
            if (e.PropertyName == nameof(GrammarWarningsViewModel.ShownCount)) ShowWhereOnlyWhenNamed();
        };
        ShowWhereOnlyWhenNamed();
    }

    public GrammarViewModel Grammar { get; }

    // A column of "Not named by the parser" on every row says nothing, so it steps aside for the problem text.
    private void ShowWhereOnlyWhenNamed()
    {
        var where = this.FindControl<DataGrid>("FindingsGrid")!.Columns.FirstOrDefault(column => (string?)column.Header == "Where");
        if (where is not null) where.IsVisible = Grammar.Warnings.AnyShownWhere;
    }
}
