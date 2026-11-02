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
/// The Trial's words reduced to how many fell in each cell and a digest of their spellings; the word rows are not
/// kept, so the Proposal's side stays a few hundred bytes however many words the Trial measured.
/// </summary>
internal sealed record MatrixProposalSummary(
    WordSetDigest Words, IReadOnlyDictionary<(WordProjectStatus Row, CompareColumnKind Column), int> Cells)
{
    /// <summary>Counts each word into the cell the matrix would place it in.</summary>
    public static MatrixProposalSummary Of(IReadOnlyList<AssessmentWordResult> words)
    {
        var cells = new Dictionary<(WordProjectStatus, CompareColumnKind), int>();
        foreach (var word in words)
        {
            var placement = CompareSemantics.PlacementOf(CompareSemantics.Compare(word));
            var cell = (WordProjectStatuses.FromStanding(placement.Standing), placement.Column);
            cells[cell] = cells.GetValueOrDefault(cell) + 1;
        }
        return new MatrixProposalSummary(WordSetDigest.Of(words.Select(word => word.Word)), cells);
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

    /// <summary>Counts the loads that changed today's words, so a test can see that one which did not kept the digest.</summary>
    internal long WordsRevision => _wordsRevision;

    /// <summary>Whether the words listed under the matrix are today's, which the Proposal's view does not list yet.</summary>
    public bool ShowsTodayWords => _proposalView != MarkupView.Proposal;

    /// <summary>Whether the fix-first list shows: it lists today's words, so not in the Proposal's own view.</summary>
    public bool ShowsFixFirst => HasFixFirst && ShowsTodayWords;

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

    /// <summary>Draws the matrix's numbers for the bar's view; reads nothing and keeps the selection and the lists.</summary>
    internal void ShowProposal(MarkupView view, MatrixSideState state, MatrixProposalSummary? summary)
    {
        _proposalView = view;
        _proposalState = state;
        _proposalSummary = state == MatrixSideState.Held ? summary : null;
        ApplyProposalNumbers();
        OnPropertyChanged(nameof(ShowsTodayWords));
        OnPropertyChanged(nameof(ShowsFixFirst));
    }

    // Also run after every load, so a new today is checked against the Proposal side already held.
    private void ApplyProposalNumbers()
    {
        var paired = ProposalWordsMatch == true;
        MatrixShown Shown(int? proposal) => new(_proposalView, paired, _proposalState, proposal);
        int? Of(WordProjectStatus? row, CompareColumnKind? column) => _proposalSummary is { } summary
            ? Cells.Where(cell => (row is null || cell.Row == row) && (column is null || cell.Column == column))
                .Sum(cell => summary.In(cell.Row, cell.Column)) : null;
        foreach (var cell in Cells) cell.ShowProposal(Shown(_proposalSummary?.In(cell.Row, cell.Column)));
        foreach (var row in Rows) row.ShowProposal(Shown(Of(row.Row, null)));
        foreach (var column in Columns) column.ShowProposal(Shown(Of(null, column.Column)));
        OnPropertyChanged(nameof(ProposalWordsMatch));
        OnPropertyChanged(nameof(ShowsFixFirst));
    }
}

public sealed partial class CompareCellViewModel
{
    private MatrixShown _shown;

    /// <summary>Whether this cell's count differs from the Proposal's, over the same words.</summary>
    public bool IsMoved => _shown.Moved(WordCount);

    /// <summary>No word falls here in the number drawn, so the cell is faint: there to read, but not asking for attention.</summary>
    public bool IsEmpty => _shown.IsZero(WordCount);

    internal void ShowProposal(MatrixShown shown)
    {
        _shown = shown;
        OnPropertyChanged(nameof(CountNumberText));
        OnPropertyChanged(nameof(IsMoved));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(AccessibleName));
    }

    private string WithProposal(string name) => !_shown.Changes ? name : string.Format(
        CultureInfo.CurrentCulture, MotifAppStrings.CompareCellProposalAccessibleNameFormat, name, ShownNumber);

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
