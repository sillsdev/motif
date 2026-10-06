using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The Texts page: Compare, Analyze texts and fixed word lists, with shared pending changes above them.</summary>
public sealed partial class TextsPage : UserControl
{
    public TextsPage(TextsPageModel page) : this(page, null)
    {
    }

    internal TextsPage(TextsPageModel page, SettingsViewModel? settings)
    {
        ArgumentNullException.ThrowIfNull(page);
        DataContext = page;
        AvaloniaXamlLoader.Load(this);

        Host("CompareHost").Content = new ComparePanel(page.Assess.Compare);
        var selection = new SelectionPanel(page.Selection, page.Words, settings);
        Host("SelectionHost").Content = selection;
        Host("AnalyzeReaderHost").Content = new ResultsInTextPanel(page.ResultsInText);
        Host("WordListHost").Content = new TextWordsPanel(page.Words);
        Host("ListsHost").Content = new TextsListsPanel(page.TextsLists);
        Host("DifferenceHost").Content = new DifferencePanel(page.Assess.Difference);

        // One prompt, moved between its two places, so the page never holds two buttons of the same name.
        var prompt = new Border { Classes = { "card" }, Child = new ParsePrompt { DataContext = page.Context } };
        var above = Host("AnalyzeParsePromptHost");
        var centred = Host("CentredParsePromptHost");
        void Place()
        {
            var host = page.ShowAnalyzeTexts ? above : centred;
            if (ReferenceEquals(host.Content, prompt)) return;
            (host == above ? centred : above).Content = null;
            host.Content = prompt;
        }
        Place();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TextsPageModel.ShowAnalyzeTexts)) Place();
        };
    }

    private ContentControl Host(string name) =>
        this.FindControl<ContentControl>(name)
        ?? throw new InvalidOperationException($"TextsPage.axaml has no element named '{name}'.");
}
