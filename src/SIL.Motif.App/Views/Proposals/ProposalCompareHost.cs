using System.ComponentModel;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>
/// Fills the Proposals page's comparison slot with the comparison of exactly the pair a person checked, and lets go of
/// it when the slot is cleared by Back, a new selection, a changed check or a closed document.
/// </summary>
public sealed class ProposalCompareHost
{
    private readonly ProposalsPageModel _page;
    private ProposalCompareViewModel? _comparison;
    private ProposalComparePanel? _panel;

    private ProposalCompareHost(ProposalsPageModel page)
    {
        _page = page;
        page.ComparisonRequested += OnRequested;
        page.PropertyChanged += OnPageChanged;
    }

    /// <summary>Makes the page offer Compare and show the comparison; the page is returned for chaining.</summary>
    public static ProposalsPageModel Attach(ProposalsPageModel page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _ = new ProposalCompareHost(page);
        return page;
    }

    private void OnRequested(IReadOnlyList<ProposalDocumentViewModel> pair)
    {
        if (pair.Count != 2) return;
        Release();
        _comparison = new ProposalCompareViewModel(_page.Context, pair, () => _page.ComparisonContent = null,
            card => _page.TryAgainCommand.ExecuteAsync(card));
        _panel = new ProposalComparePanel(_comparison);
        _page.ComparisonContent = _panel;
    }

    private void OnPageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProposalsPageModel.ComparisonContent) && !ReferenceEquals(_page.ComparisonContent, _panel))
            Release();
    }

    private void Release()
    {
        _comparison?.Dispose();
        _comparison = null;
        _panel = null;
    }
}
