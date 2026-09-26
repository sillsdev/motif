using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Texts page: Compare, Analyze texts and fixed word lists, with shared pending changes above them.</summary>
public sealed partial class TextsPage : UserControl
{
    public TextsPage(TextsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);

        Host("CompareHost").Content = new ComparePanel(page.Assess.Compare);
        var selection = new SelectionPanel(page.Selection, page.Words);
        Host("SelectionHost").Content = selection;
        Host("AnalyzeReaderHost").Content = new ResultsInTextPanel(page.ResultsInText);
        Host("WordListHost").Content = new TextWordsPanel(page.Words);
        Host("ListsHost").Content = new TextsListsPanel(page.TextsLists);
        Host("DifferenceHost").Content = new DifferencePanel(page.Assess.Difference);
    }

    private ContentControl Host(string name) =>
        this.FindControl<ContentControl>(name)
        ?? throw new InvalidOperationException($"TextsPage.axaml has no element named '{name}'.");
}
