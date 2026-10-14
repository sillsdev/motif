using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The selected Warnings finding and its linked evidence, hosted in the Inspector.</summary>
public sealed partial class WarningsInspectorPanel : UserControl
{
    /// <summary>Creates the Inspector controls for <paramref name="page"/>.</summary>
    public WarningsInspectorPanel(WarningsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        Page = page;
        Grammar = page.Grammar;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        ExactWordPresentationHost = CreateHost("WarningsInspectorWordRows");
        MembershipCandidatePresentationHost = CreateHost("WarningsInspectorMembershipRows");
        SpellingCandidatePresentationHost = CreateHost("WarningsInspectorSpellingRows");
    }

    /// <summary>The Warnings page model that owns selection and area state.</summary>
    public WarningsPageModel Page { get; }

    /// <summary>The grammar data shown by the selected finding.</summary>
    public GrammarViewModel Grammar { get; }

    /// <summary>The host for exact word matches in the selected finding.</summary>
    public IWordPresentationHost ExactWordPresentationHost { get; }

    /// <summary>The host for membership candidate words in the selected finding.</summary>
    public IWordPresentationHost MembershipCandidatePresentationHost { get; }

    /// <summary>The host for spelling candidate words in the selected finding.</summary>
    public IWordPresentationHost SpellingCandidatePresentationHost { get; }

    private IWordPresentationHost CreateHost(string listName) =>
        new WordListPresentationHost<WordRowViewModel>(
            this.FindControl<ItemsControl>(listName)!, row => row.Presentation, WordListCardSections.Warning);
}
