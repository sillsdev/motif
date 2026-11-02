using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>Whose cells the words under the matrix are listed by.</summary>
internal enum WordListBasis
{
    /// <summary>Today's cells.</summary>
    Today,

    /// <summary>A word listed if either side puts it in a chosen cell, which only a pair over the same words can do.</summary>
    Both,

    /// <summary>The Proposal's cells alone.</summary>
    Proposal,
}

public sealed partial class CompareViewModel
{
    private byte[]? _alignedCells;
    private MatrixProposalSummary? _alignedFor;
    private long _alignedRevision = -1;

    private WordListBasis _listedBasis;
    private MatrixProposalSummary? _listedSummary;
    private long _listedRevision = -1;

    // The words already listed came from this basis, Proposal side and load, so a flip need not sort them again.
    private bool ListingStands => _listedBasis == ListedBasis && _listedRevision == _wordsRevision &&
        (ListedBasis == WordListBasis.Today || ReferenceEquals(_listedSummary, _proposalSummary));

    // A pair with no cell chosen lists every word in today's order, as Today does: the same listing.
    private WordListBasis ListedBasis => ListBasis == WordListBasis.Both && !Cells.Any(cell => cell.IsSelected)
        ? WordListBasis.Today : ListBasis;

    private bool HoldsProposalSide => _proposalSummary is not null && _proposalState == MatrixSideState.Held;

    /// <summary>Which side's cells list the words under the matrix in the bar's current view.</summary>
    internal WordListBasis ListBasis => !HoldsProposalSide ? WordListBasis.Today : _proposalView switch
    {
        MarkupView.Proposal => WordListBasis.Proposal,
        MarkupView.TodayToProposal when ProposalWordsMatch == true => WordListBasis.Both,
        _ => WordListBasis.Today,
    };

    /// <summary>
    /// Whether the card of listed words shows: always for today's words, and for the Proposal's only once its side
    /// is read, because without it there are no words to list.
    /// </summary>
    public bool ShowsWordList => _proposalView != MarkupView.Proposal || HoldsProposalSide;

    /// <summary>Whether each listed word says where the Proposal put it.</summary>
    public bool ShowsProposalColumn => HoldsProposalSide && _proposalView != MarkupView.Today;

    /// <summary>The columns the listed words show: today's, today's beside the Proposal's, or the Proposal's alone.</summary>
    internal WordListSchema WordListSchema => !ShowsProposalColumn ? WordListSchemas.All
        : _proposalView == MarkupView.Proposal ? WordListSchemas.WithProposalOnly : WordListSchemas.AllWithProposal;

    // The Proposal's cell for each of today's words by position, so a huge Selection is searched once.
    private byte[] AlignedProposalCells()
    {
        var summary = _proposalSummary!;
        if (_alignedCells is not null && ReferenceEquals(_alignedFor, summary) && _alignedRevision == _wordsRevision)
            return _alignedCells;
        var aligned = new byte[_all.Count];
        for (var index = 0; index < aligned.Length; index++)
            aligned[index] = summary.Index.TryFind(_all[index].Word, out var facts) ? facts.Cell : ProposalWordIndex.NoCell;
        ProposalWork++;
        _alignedCells = aligned;
        _alignedFor = summary;
        _alignedRevision = _wordsRevision;
        return aligned;
    }

    private Dictionary<(WordProjectStatus Row, CompareColumnKind Column), int?>? _placesByCell;
    private MatrixProposalSummary? _placesFor;
    private long _placesRevision = -1;

    // Places per cell from the words the Proposal puts there; null where the Trial has words today lacks.
    private Dictionary<(WordProjectStatus Row, CompareColumnKind Column), int?> ProposalPlaces()
    {
        var summary = _proposalSummary!;
        if (_placesByCell is not null && ReferenceEquals(_placesFor, summary) && _placesRevision == _evidenceRevision)
            return _placesByCell;
        var aligned = AlignedProposalCells();
        var places = new int[byte.MaxValue + 1];
        var matched = new int[byte.MaxValue + 1];
        for (var index = 0; index < aligned.Length; index++)
        {
            places[aligned[index]] += _all[index].Occurrences ?? 0;
            matched[aligned[index]]++;
        }
        var byCell = new Dictionary<(WordProjectStatus Row, CompareColumnKind Column), int?>();
        foreach (var cell in Cells)
        {
            var packed = ProposalWordIndex.Pack(cell.Row, cell.Column);
            byCell[(cell.Row, cell.Column)] = matched[packed] == summary.In(cell.Row, cell.Column) ? places[packed] : null;
        }
        _placesByCell = byCell;
        _placesFor = summary;
        _placesRevision = _evidenceRevision;
        return byCell;
    }

    // Drops what was made from a Proposal side that is no longer shown, so its words are not kept alive.
    private void ForgetProposalSide()
    {
        _alignedCells = null;
        _alignedFor = null;
        _placesByCell = null;
        _placesFor = null;
        _listedSummary = null;
    }

    // Today's words in the chosen cells, or the Proposal's, in Assessment order; no chosen cell lists every word.
    private IEnumerable<AssessmentRowFacts> ListedByCells(HashSet<(WordProjectStatus Row, CompareColumnKind Column)> chosen)
    {
        var basis = ListBasis;
        if (basis == WordListBasis.Today || basis == WordListBasis.Both && chosen.Count == 0)
            return chosen.Count == 0 ? _all : _all.Where(word => chosen.Contains((word.Row, word.Column)));
        var aligned = AlignedProposalCells();
        return _all.Where((word, index) =>
        {
            var inProposal = aligned[index] != ProposalWordIndex.NoCell;
            var proposalChosen = inProposal && chosen.Contains(ProposalWordIndex.Unpack(aligned[index]));
            if (basis == WordListBasis.Proposal) return inProposal && (chosen.Count == 0 || proposalChosen);
            return chosen.Count == 0 || proposalChosen || chosen.Contains((word.Row, word.Column));
        });
    }

    // The work the order and the Time column read: the Proposal's own in its view, else today's.
    private (long? Steps, int? Elapsed) OrderedWork(AssessmentRowFacts word) =>
        ListBasis == WordListBasis.Proposal && _proposalSummary!.Index.TryFind(word.Word, out var facts)
            ? (facts.WorkSteps, facts.ElapsedMs) : (word.Source.WorkSteps, word.Source.ElapsedMs);

    // How many words the Proposal counts in the chosen cells that today's matrix lacks, which cannot be listed.
    private int WordsCountedNotListed(CompareCellViewModel[] chosen)
    {
        if (ListBasis != WordListBasis.Proposal || ProposalWordsMatch == true) return 0;
        var listable = ListedByCells(chosen.Select(cell => (cell.Row, cell.Column)).ToHashSet()).Count();
        return Math.Max(0, WordsInChosenCells(chosen) - listable);
    }

    private int WordsInChosenCells(CompareCellViewModel[] chosen) => ListBasis switch
    {
        WordListBasis.Proposal => chosen.Length == 0 ? _proposalSummary!.Cells.Values.Sum()
            : chosen.Sum(cell => _proposalSummary!.In(cell.Row, cell.Column)),
        WordListBasis.Both => ListedByCells(chosen.Select(cell => (cell.Row, cell.Column)).ToHashSet()).Count(),
        _ => chosen.Length == 0 ? TotalCount : chosen.Sum(cell => cell.WordCount),
    };

    // Says where the Proposal put a word, or that its try did not measure it; null when no side is shown.
    private (WordProposalCell? Cell, string? Time) ProposalOf(AssessmentRowFacts facts)
    {
        if (!ShowsProposalColumn) return (null, null);
        if (!_proposalSummary!.Index.TryFind(facts.Word, out var proposed))
            return (new WordProposalCell(null, MotifAppStrings.TextsWordNotMeasured, _proposalView == MarkupView.Proposal), null);
        var (_, column) = ProposalWordIndex.Unpack(proposed.Cell);
        var cell = new WordProposalCell(Mark.Of(WindowWords.OutcomeOf(column)), ColumnLabelOf(column), _proposalView == MarkupView.Proposal);
        return (cell, _proposalView == MarkupView.Proposal ? StepsText(proposed) : null);
    }

    private static string StepsText(ProposalWordFacts proposed) =>
        proposed.HidesSteps ? string.Empty
        : proposed.WorkSteps is { } steps ? PluralText.Format(MotifAppStrings.WorkStepsPattern, steps)
        : MotifAppStrings.WorkStepsNotRecorded;

    private void ShowProposalOn(CompareWordViewModel word)
    {
        var (cell, time) = ProposalOf(word.Facts);
        word.ShowProposal(cell, time);
    }

    // A card left open for a word the view no longer lists would stay pinned and unseen, so it is closed.
    private void CloseCardsOfWordsNoLongerListed()
    {
        foreach (var model in _wordModels.Models.Where(model => model.IsExpanded).ToArray())
            if (!_listed.Any(facts => ReferenceEquals(facts, model.Facts))) model.IsExpanded = false;
    }

    // Keeps the rows already made, so a flip of the view repaints them without making new ones.
    private void ShowProposalOnRows()
    {
        foreach (var word in _wordModels.Models.ToArray()) ShowProposalOn(word);
    }
}

public sealed partial class CompareWordViewModel
{
    private WordProposalCell? _withProposal;
    private string? _proposalTime;

    internal void ShowProposal(WordProposalCell? cell, string? time)
    {
        if (_withProposal == cell && _proposalTime == time) return;
        _withProposal = cell;
        _proposalTime = time;
        OnPropertyChanged(nameof(MatrixPresentation));
    }
}
