using System.Globalization;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

public sealed partial class DifferenceViewModel
{
    /// <summary>
    /// Holds the words the open Proposal moves in place of two runs' words, or lets them go with null. Only the move
    /// counts are made here; the words of a move are made when it is chosen, and none outlive the set.
    /// </summary>
    internal void LoadProposalMoves(ProposalMoveSet? set)
    {
        if (ReferenceEquals(set, _proposalMoves)) return;
        _proposalMoves = set;
        _listed.Clear();
        Moves.Clear();
        SelectedMove = null;
        (ComparedCount, MovedCount, RegressedCount, OnlyInOneCount) = (0, 0, 0, 0);
        BeforeLabel = set is null ? string.Empty : MotifAppStrings.DifferenceProposalFromLabel;
        AfterLabel = set is null ? string.Empty : MotifAppStrings.DifferenceProposalToLabel;
        if (set is not null) MakeProposalMoves(set);
        foreach (var property in new[] { nameof(HasDifference), nameof(ComparedCount), nameof(MovedCount),
            nameof(RegressedCount), nameof(Summary), nameof(OnlyInOneText), nameof(RunComparisonText), nameof(EmptyText) })
            OnPropertyChanged(property);
        SelectedMove = Moves.FirstOrDefault();
    }

    private void MakeProposalMoves(ProposalMoveSet set)
    {
        ComparedCount = set.Compared;
        OnlyInOneCount = set.TodayOnly + set.ProposalOnly;
        var moves = set.Moves.Select(move =>
        {
            var from = ProposalWordIndex.Unpack(move.From);
            var to = ProposalWordIndex.Unpack(move.To);
            var meaning = CompareViewModel.MeaningOf(to.Row, to.Column);
            var cell = string.Format(CultureInfo.CurrentCulture, MotifAppStrings.DifferenceProposalCellFormat,
                meaning.Label, CompareViewModel.RowLabelOf(to.Row));
            return new MoveViewModel(from, to, move.Indices.Length, () => WordsOf(set, move.Indices, to, cell), true);
        }).OrderBy(move => move.Kind).ThenByDescending(move => move.Count);
        foreach (var move in moves) Moves.Add(move);
        MovedCount = Moves.Sum(move => move.Count);
        RegressedCount = Moves.Where(move => move.Kind == MoveKind.Regressed).Sum(move => move.Count);
    }

    // Most places first, then the spelling; only the order is made here, the rows when they are read.
    private LazyMovedWords WordsOf(ProposalMoveSet set, int[] indices,
        (WordProjectStatus Row, CompareColumnKind Column) to, string cell)
    {
        var ordered = (int[])indices.Clone();
        Array.Sort(ordered, (left, right) =>
        {
            var byPlaces = (set.Today[right].OccurrenceCount ?? 0).CompareTo(set.Today[left].OccurrenceCount ?? 0);
            return byPlaces != 0 ? byPlaces : string.CompareOrdinal(set.Today[left].Word, set.Today[right].Word);
        });
        return new LazyMovedWords(ordered, index => new MovedWordViewModel(set.Today[index], to, cell, _listed.Get));
    }
}
