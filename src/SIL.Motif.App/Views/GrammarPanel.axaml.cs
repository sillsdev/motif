using System.Collections;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;

namespace SIL.Motif.App.Views;

/// <summary>The Grammar page's findings about the grammar as a whole, bound to <see cref="Grammar"/>.</summary>
public sealed partial class GrammarPanel : UserControl
{
    /// <summary>Creates the grammar panel with the full table for standalone grammar views.</summary>
    public GrammarPanel(GrammarViewModel grammar)
        : this(grammar, null)
    {
    }

    /// <summary>Creates the Warnings page panel with its area-filtered table.</summary>
    public GrammarPanel(WarningsPageModel page)
        : this(page.Grammar, page)
    {
        ArgumentNullException.ThrowIfNull(page);
    }

    private GrammarPanel(GrammarViewModel grammar, WarningsPageModel? page)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        Grammar = grammar;
        TableEntries = page is null ? grammar.Warnings.TableEntries : page.FilteredTableEntries;
        ShowInlineSelectionDetails = page is null;
        ShowInlineFilters = page is null;
        InlineSelectionDetails = page is null
            ? grammar.Warnings.SelectedDetails
            : Array.Empty<GrammarWarningRowViewModel>();
        OpenFindingDetailsCommand = new RelayCommand<GrammarWarningRowViewModel>(OpenFindingDetails);
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        var warningRows = this.FindControl<ListBox>("GrammarPanelRowsItems")!;
        DenseTable.Attach(warningRows, row => OpenFindingDetails(row as GrammarWarningRowViewModel));
        ExactWordPresentationHost = CreateHost("GrammarPanelWordRowsItems");
        MembershipCandidatePresentationHost = CreateHost("GrammarPanelMembershipCandidateRowsItems");
        SpellingCandidatePresentationHost = CreateHost("GrammarPanelSpellingCandidateRowsItems");
    }

    public GrammarViewModel Grammar { get; }

    /// <summary>Opens a finding's details inline or in the page Inspector, according to this panel's owner.</summary>
    public IRelayCommand<GrammarWarningRowViewModel> OpenFindingDetailsCommand { get; }

    /// <summary>The warning group headings and rows shown by this panel.</summary>
    public IEnumerable TableEntries { get; }

    /// <summary>Whether this panel displays selected details below its table.</summary>
    public bool ShowInlineSelectionDetails { get; }

    /// <summary>Whether this panel shows the grammar filters inline.</summary>
    public bool ShowInlineFilters { get; }

    /// <summary>The selected details shown inline by a standalone grammar panel.</summary>
    public IEnumerable<GrammarWarningRowViewModel> InlineSelectionDetails { get; }

    /// <summary>The grammar warning host for its exact word matches.</summary>
    public IWordPresentationHost ExactWordPresentationHost { get; }

    /// <summary>The grammar warning host for membership candidate words.</summary>
    public IWordPresentationHost MembershipCandidatePresentationHost { get; }

    /// <summary>The grammar warning host for spelling candidate words.</summary>
    public IWordPresentationHost SpellingCandidatePresentationHost { get; }

    private IWordPresentationHost CreateHost(string listName) =>
        new WordListPresentationHost<WordRowViewModel>(
            this.FindControl<ItemsControl>(listName)!, row => row.Presentation, WordListCardSections.Warning);

    private void OpenFindingDetails(GrammarWarningRowViewModel? row)
    {
        if (row is null) return;
        Grammar.Warnings.OpenDetailsCommand.Execute(row);
        if (!ShowInlineSelectionDetails) WorkspaceLayoutHost.RequestPageInspector(this);
    }
}
