using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
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
        ExactWordPresentationHost = CreateHost("GrammarPanelWordRowsItems");
        MembershipCandidatePresentationHost = CreateHost("GrammarPanelMembershipCandidateRowsItems");
        SpellingCandidatePresentationHost = CreateHost("GrammarPanelSpellingCandidateRowsItems");
    }

    public GrammarViewModel Grammar { get; }

    /// <summary>The grammar warning host for its exact word matches.</summary>
    public IWordPresentationHost ExactWordPresentationHost { get; }

    /// <summary>The grammar warning host for membership candidate words.</summary>
    public IWordPresentationHost MembershipCandidatePresentationHost { get; }

    /// <summary>The grammar warning host for spelling candidate words.</summary>
    public IWordPresentationHost SpellingCandidatePresentationHost { get; }

    private IWordPresentationHost CreateHost(string listName) =>
        new WordListPresentationHost<WordRowViewModel>(
            this.FindControl<ItemsControl>(listName)!, row => row.Presentation, WordListCardSections.Warning);
}
