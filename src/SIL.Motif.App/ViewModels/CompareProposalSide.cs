using System.Globalization;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>What the matrix has of the open Proposal's Trial: being read, not readable, absent, or held.</summary>
internal enum MatrixSideState
{
    None,
    Reading,
    Unreadable,
    Held,
}

/// <summary>
/// The Trial's words reduced to how many fell in each cell, a digest of their spellings and an index of the values
/// the word list reads for each; the word rows are not kept, so the Proposal's side stays tens of bytes a word.
/// </summary>
internal sealed record MatrixProposalSummary(
    WordSetDigest Words, IReadOnlyDictionary<(WordProjectStatus Row, CompareColumnKind Column), int> Cells)
{
    /// <summary>What the Trial recorded for each word, found by spelling; empty for a summary built from counts alone.</summary>
    public ProposalWordIndex Index { get; init; } = ProposalWordIndex.Empty;

    /// <summary>Counts each word into the cell the matrix would place it in.</summary>
    public static MatrixProposalSummary Of(IReadOnlyList<AssessmentWordResult> words)
    {
        var cells = new Dictionary<(WordProjectStatus, CompareColumnKind), int>();
        var placed = new byte[words.Count];
        var hidesSteps = new bool[words.Count];
        for (var index = 0; index < words.Count; index++)
        {
            var comparison = CompareSemantics.Compare(words[index]);
            hidesSteps[index] = comparison.MeaningCode is "refused" or "not-parsed";
            var placement = CompareSemantics.PlacementOf(comparison);
            var cell = (WordProjectStatuses.FromStanding(placement.Standing), placement.Column);
            cells[cell] = cells.GetValueOrDefault(cell) + 1;
            placed[index] = ProposalWordIndex.Pack(cell.Item1, cell.Column);
        }
        return new MatrixProposalSummary(WordSetDigest.Of(words.Select(word => word.Word)), cells)
            { Index = ProposalWordIndex.Of(words, placed, hidesSteps) };
    }

    internal int In(WordProjectStatus row, CompareColumnKind column) => Cells.GetValueOrDefault((row, column));
}

/// <summary>How a cell, row or column draws its number in the bar's current view.</summary>
internal readonly record struct MatrixShown(MarkupView View, bool Paired, MatrixSideState State, int? Proposal)
{
    /// <summary>The number as drawn, from today's text; a side that is not there reads as lacking, never zero.</summary>
    public string Number(string today)
    {
        if (View == MarkupView.TodayToProposal) return Paired && Proposal is { } paired ? Pair(today, paired) : today;
        if (View != MarkupView.Proposal) return today;
        return Proposal is { } held ? held.ToString("N0", CultureInfo.CurrentCulture)
            : State == MatrixSideState.Reading ? "…" : MissingValue.Marker;
    }

    /// <summary>Whether the drawn number is no longer today's alone.</summary>
    public bool Changes => View == MarkupView.Proposal || View == MarkupView.TodayToProposal && Paired;

    /// <summary>Whether the number drawn is zero; a side that is not there is faint too, as nothing can be read.</summary>
    public bool IsZero(int today) => View switch
    {
        MarkupView.Proposal => Proposal is { } held ? held == 0 : State != MatrixSideState.Reading || today == 0,
        MarkupView.TodayToProposal when Paired && Proposal is { } held => today == 0 && held == 0,
        _ => today == 0,
    };

    /// <summary>Whether the pair differs, so the cell is drawn as moved.</summary>
    public bool Moved(int today) => View == MarkupView.TodayToProposal && Paired && Proposal is { } held && held != today;

    private static string Pair(string today, int proposal) => string.Format(CultureInfo.CurrentCulture,
        MotifAppStrings.OverviewPairFormat, today, proposal.ToString("N0", CultureInfo.CurrentCulture));
}

public sealed partial class CompareViewModel
{
    private MarkupView _proposalView = MarkupView.Today;
    private MatrixSideState _proposalState = MatrixSideState.None;
    private MatrixProposalSummary? _proposalSummary;
    private WordSetDigest _todayWords;
    private long _wordsRevision;
    private long _todayWordsRevision = -1;

    /// <summary>Counts the digests and alignments made for a Proposal, so a test can see that none is made without one.</summary>
    internal int ProposalWork { get; private set; }

    /// <summary>Counts the times the listed words were filtered and sorted, so a test can see a flip that did not.</summary>
    internal int FilterRuns { get; private set; }

    /// <summary>Counts the loads that changed today's words, so a test can see that one which did not kept the digest.</summary>
    internal long WordsRevision => _wordsRevision;

    /// <summary>Whether the Proposal's own view is not shown, so the controls built on today's words (the chips and fix-first list) show.</summary>
    public bool ShowsTodayWords => _proposalView != MarkupView.Proposal;

    /// <summary>Whether the fix-first list shows: it lists today's words, so not in the Proposal's own view.</summary>
    public bool ShowsFixFirst => HasFixFirst && ShowsTodayWords;

    /// <summary>How a number reads in the bar's view, given the Proposal's value for it, or null where it has none.</summary>
    internal MatrixShown ShownFor(int? proposal) => new(_proposalView, ProposalWordsMatch == true, _proposalState, proposal);

    /// <summary>The words the Proposal puts in these cells, or null while its side is not held.</summary>
    internal int? ProposalWordsIn(IReadOnlyList<TextsListCell> cells) => _proposalSummary is { } summary
        ? cells.Sum(cell => summary.In(cell.Row, cell.Column)) : null;

    /// <summary>
    /// How many more words the Proposal puts in the Same column than today has there, from the counts already held;
    /// null unless its side is held and covers the same words, since two different word sets have no change to name.
    /// </summary>
    internal int? ProposalSameChange() => _proposalSummary is { } summary && ProposalWordsMatch == true
        ? Cells.Where(cell => cell.Column == CompareColumnKind.Match)
            .Sum(cell => summary.In(cell.Row, cell.Column) - cell.WordCount) : null;

    /// <summary>The places of the words the Proposal puts in these cells, or null where any cell's are not known.</summary>
    internal int? ProposalPlacesIn(IReadOnlyList<TextsListCell> cells)
    {
        var chosen = cells.ToHashSet();
        var total = 0;
        foreach (var cell in Cells.Where(cell => chosen.Contains(new TextsListCell(cell.Row, cell.Column))))
        {
            if (cell.ProposalPlaceCount is not { } places) return null;
            total += places;
        }
        return total;
    }

    /// <summary>
    /// Whether the held Proposal side covers the same words as the matrix, or null when there is nothing to check;
    /// today's words are hashed once per load, by revision, and only when a Proposal side is held.
    /// </summary>
    internal bool? ProposalWordsMatch => _proposalSummary is { } summary && _proposalState == MatrixSideState.Held
        ? summary.Words == TodayWords : null;

    private WordSetDigest TodayWords
    {
        get
        {
            if (_todayWordsRevision == _wordsRevision) return _todayWords;
            _todayWords = WordSetDigest.Of(_all.Select(word => word.Word));
            ProposalWork++;
            _todayWordsRevision = _wordsRevision;
            return _todayWords;
        }
    }

    // A load that brings the same words in the same order keeps the digest; any other load makes it stale.
    private void NoteWordsOfLoad(IReadOnlyList<AssessmentRowFacts> incoming)
    {
        var same = incoming.Count == _all.Count;
        for (var index = 0; same && index < incoming.Count; index++)
            same = string.Equals(incoming[index].Word, _all[index].Word, StringComparison.Ordinal);
        if (!same) _wordsRevision++;
    }

    /// <summary>
    /// Draws the matrix's numbers and the listed words' columns for the bar's view; reads nothing, keeps the selection,
    /// the sort and the search, and leaves the list itself alone when the same words stay listed.
    /// </summary>
    internal void ShowProposal(MarkupView view, MatrixSideState state, MatrixProposalSummary? summary)
    {
        var basis = ListBasis;
        _movedCount = null;
        _proposalView = view;
        _proposalState = state;
        _proposalSummary = state == MatrixSideState.Held ? summary : null;
        if (_proposalSummary is null) ForgetProposalSide();
        ApplyProposalNumbers();
        if (!ListingStands) ApplyFilter(announce: false);
        CloseCardsOfWordsNoLongerListed();
        ShowProposalOnRows();
        if (ListBasis != basis) RefreshShared(Cells.Any(cell => cell.IsSelected));
        foreach (var property in new[] { nameof(ShowsTodayWords), nameof(ShowsFixFirst), nameof(ShowsWordList),
            nameof(ShowsProposalColumn), nameof(WordListSchema), nameof(HasCheckedWords),
            nameof(CanRerun), nameof(ListSummary), nameof(ShowsMovedChip), nameof(IsMovedFilterOn), nameof(MovedChipCount),
            nameof(MovedChipAccessibleName), nameof(ListHeading) })
            OnPropertyChanged(property);
    }

    // Also run after every load, so a new today is checked against the Proposal side already held.
    private void ApplyProposalNumbers()
    {
        var paired = ProposalWordsMatch == true;
        MatrixShown Shown(int? proposal) => ShownFor(proposal);
        int? Of(WordProjectStatus? row, CompareColumnKind? column) => _proposalSummary is { } summary
            ? Cells.Where(cell => (row is null || cell.Row == row) && (column is null || cell.Column == column))
                .Sum(cell => summary.In(cell.Row, cell.Column)) : null;
        var places = HoldsProposalSide && (_proposalView == MarkupView.Proposal ||
            _proposalView == MarkupView.TodayToProposal && paired) ? ProposalPlaces() : null;
        foreach (var cell in Cells)
            cell.ShowProposal(Shown(_proposalSummary?.In(cell.Row, cell.Column)), places?.GetValueOrDefault((cell.Row, cell.Column)));
        foreach (var row in Rows) row.ShowProposal(Shown(Of(row.Row, null)));
        foreach (var column in Columns) column.ShowProposal(Shown(Of(null, column.Column)));
        OnPropertyChanged(nameof(ProposalWordsMatch));
        OnPropertyChanged(nameof(ShowsFixFirst));
    }
}

public sealed partial class CompareCellViewModel
{
    private MatrixShown _shown;
    private int? _proposalPlaces;

    /// <summary>The places of the words the Proposal puts here, or null where they are not known or not drawn.</summary>
    internal int? ProposalPlaceCount => _proposalPlaces;

    /// <summary>Whether this cell's count differs from the Proposal's, over the same words.</summary>
    public bool IsMoved => _shown.Moved(WordCount);

    /// <summary>No word falls here in the number drawn, so the cell is faint: there to read, but not asking for attention.</summary>
    public bool IsEmpty => _shown.IsZero(WordCount);

    internal void ShowProposal(MatrixShown shown, int? proposalPlaces)
    {
        _shown = shown;
        _proposalPlaces = proposalPlaces;
        OnPropertyChanged(nameof(CountNumberText));
        OnPropertyChanged(nameof(CountUnitText));
        OnPropertyChanged(nameof(PlacesText));
        OnPropertyChanged(nameof(PlacesNote));
        OnPropertyChanged(nameof(IsMoved));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(IsEmptyImpossible));
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private string WithProposal(string name) => !_shown.Changes ? name : !HasPlaces ? string.Format(
        CultureInfo.CurrentCulture, MotifAppStrings.CompareCellProposalAccessibleNameFormat, name, ShownWords)
        : string.Format(CultureInfo.CurrentCulture, MotifAppStrings.CompareCellProposalPlacesAccessibleNameFormat,
            name, ShownWords, PlacesNote ?? ShownPlaces);

    // The number drawn with its unit, so the name says words; a side still being read has no unit to say.
    private string ShownWords => _shown.Proposal is null ? ShownNumber : ShownNumber + " " +
        PluralText.Format(MotifAppStrings.CompareWordUnitPattern, UnitCount);

    /// <summary>Why the Proposal's places are blank, for a tooltip and the accessible name; null when they show.</summary>
    public string? PlacesNote => HasPlaces && _shown.View == MarkupView.Proposal && _proposalPlaces is null &&
        _shown.State != MatrixSideState.Reading
        ? _shown.State == MatrixSideState.Held ? MotifAppStrings.TextsListPlacesNotCounted : MotifAppStrings.TextsWordNotMeasured
        : null;

    // Today's places, today's then the Proposal's in the pair, or the Proposal's alone in its view.
    private string ShownPlaces
    {
        get
        {
            var today = PlacesTextOf(OccurrenceCount, PlacesFromWordList);
            if (!_shown.Changes) return today;
            if (_proposalPlaces is not { } held)
                return _shown.View != MarkupView.Proposal ? today : _shown.State == MatrixSideState.Reading ? "…" : string.Empty;
            var proposal = PlacesTextOf(held, PlacesFromWordList);
            return _shown.View == MarkupView.Proposal ? proposal : string.Format(CultureInfo.CurrentCulture,
                MotifAppStrings.OverviewPairFormat, OccurrenceCount.ToString("N0", CultureInfo.CurrentCulture), proposal);
        }
    }

    // The unit follows the Proposal's count where that is the number drawn, so one word is not '1 words'.
    private int UnitCount => _shown.Changes && _shown.Proposal is { } held ? held : WordCount;

    private string ShownNumber => _shown.Number(WordCount.ToString("N0", CultureInfo.CurrentCulture));
}

public sealed partial class CompareRowViewModel
{
    private MatrixShown _shown;

    internal void ShowProposal(MatrixShown shown)
    {
        _shown = shown;
        OnPropertyChanged(nameof(CountText));
    }

    private string ShownCount
    {
        get
        {
            if (!_shown.Changes) return PluralText.Format(MotifAppStrings.CompareWordsPattern, Count);
            var number = _shown.Number(Count.ToString("N0", CultureInfo.CurrentCulture));
            return _shown.Proposal is { } proposal
                ? number + " " + PluralText.Format(MotifAppStrings.CompareWordUnitPattern, proposal) : number;
        }
    }
}

public sealed partial class CompareColumnViewModel
{
    private MatrixShown _shown;

    internal void ShowProposal(MatrixShown shown)
    {
        _shown = shown;
        OnPropertyChanged(nameof(CountText));
    }

    private string ShownCount => _shown.Number(Count.ToString("N0", CultureInfo.CurrentCulture));
}
