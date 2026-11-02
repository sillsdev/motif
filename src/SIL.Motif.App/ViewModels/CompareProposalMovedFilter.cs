using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

public sealed partial class CompareViewModel
{
    private bool _movedChosen;
    private (long Cells, long Revision, int Count)? _movedCount;
    private RelayCommand? _toggleMovedFilter;

    /// <summary>
    /// Whether the word list is narrowed to the words this Proposal moves: it needs the Proposal's side and a view that
    /// shows it, so the Today view lists everything again without forgetting the choice.
    /// </summary>
    internal bool MovedFilterOn => _movedChosen && CanFilterMoved;

    private bool CanFilterMoved => HoldsProposalSide && _proposalView != MarkupView.Today;

    /// <summary>Whether the chip shows: the Proposal moves a word in the chosen cells, or its filter is the one in force.</summary>
    public bool ShowsMovedChip => CanFilterMoved && (MovedChipCount > 0 || _movedChosen);

    /// <summary>Whether the words listed are only the ones the Proposal moves.</summary>
    public bool IsMovedFilterOn => MovedFilterOn;

    /// <summary>
    /// How many words in the chosen cells the Proposal moves to another cell, over the words both sides have; a word only
    /// one side has is not counted, and nothing reads as zero for a side that lacks it. Counted from the cells already
    /// aligned, once for a choice and load.
    /// </summary>
    public int MovedChipCount
    {
        get
        {
            if (!CanFilterMoved) return 0;
            var chosen = Cells.Where(cell => cell.IsSelected).Select(cell => (cell.Row, cell.Column)).ToHashSet();
            var key = chosen.Aggregate(0L, (mask, cell) => mask | 1L << ProposalWordIndex.Pack(cell.Row, cell.Column));
            if (_movedCount is { } held && held.Cells == key && held.Revision == _wordsRevision) return held.Count;
            var count = ListedByCells(chosen, onlyMoved: true).Count();
            _movedCount = (key, _wordsRevision, count);
            return count;
        }
    }

    /// <summary>What the chip says to a screen reader: whose cells the words moved between, and how many.</summary>
    public string MovedChipAccessibleName => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.TextsMovedChipAccessibleNameFormat, MotifAppStrings.TextsMovedChipLabel,
        PluralText.Format(MotifAppStrings.TextsMovedChipWordsPattern, MovedChipCount));

    /// <summary>Chooses or drops the Moved By This Proposal filter; it reads nothing and keeps the sort, search and cells.</summary>
    public IRelayCommand ToggleMovedFilterCommand => _toggleMovedFilter ??= new RelayCommand(ToggleMovedFilter);

    private void ToggleMovedFilter()
    {
        if (!CanFilterMoved) return;
        _movedChosen = !_movedChosen;
        ApplyFilter();
        RaiseMovedChip();
    }

    private void RaiseMovedChip()
    {
        foreach (var property in new[] { nameof(ShowsMovedChip), nameof(IsMovedFilterOn), nameof(MovedChipCount),
            nameof(MovedChipAccessibleName), nameof(ListHeading), nameof(ListSummary) })
            OnPropertyChanged(property);
    }
}
