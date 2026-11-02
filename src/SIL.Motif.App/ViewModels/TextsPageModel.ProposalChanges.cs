namespace SIL.Motif.App.ViewModels;

public sealed partial class TextsPageModel
{
    /// <summary>Whether the pair and the Proposal view have the Proposal's side to compare today with, so What Changed lists its moves.</summary>
    public bool ShowsProposalMoves => Context.Lens is not null &&
        ProposalViewScope.ViewOf(Context) != MarkupView.Today && Assess.Compare.ProposalWordsMatch is not null;

    // Finds the moves only while What Changed is shown beside the Proposal, and drops stale ones.
    private void ShowProposalMoves()
    {
        var current = Assess.Compare.HeldProposalMoves;
        if (ShowsProposalMoves && Tab == TextsTab.WhatChanged && Context.CurrentPage == WorkspacePage.Texts) ProposalChanges.LoadProposalMoves(Assess.Compare.ProposalMoves());
        else if (ProposalChanges.ProposalMoves is { } loaded && !ReferenceEquals(loaded, current))
            ProposalChanges.LoadProposalMoves(null);
        OnPropertyChanged(nameof(ShowsProposalMoves));
        OnPropertyChanged(nameof(ShowWhatChangedContent));
        OnPropertyChanged(nameof(ShowProposalMovesContent));
    }
}
