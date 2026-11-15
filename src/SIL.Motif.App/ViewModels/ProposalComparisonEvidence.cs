using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

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

/// <summary>The words that went from one cell today to another cell with the open Proposal, by their place in today's list.</summary>
/// <param name="From">The cell today, packed as <see cref="ProposalWordIndex.Pack"/> does.</param>
/// <param name="To">The cell with the Proposal.</param>
/// <param name="Indices">Positions in today's words; four bytes a moved word.</param>
internal readonly record struct ProposalMove(byte From, byte To, int[] Indices);

/// <summary>
/// Where the open Proposal moves today's words: only the words both today's matrix and the Proposal's try have are
/// compared, and the words only one side has are counted. Holds positions and counts, never word rows.
/// </summary>
internal sealed class ProposalMoveSet(IReadOnlyList<AssessmentRowFacts> today, MatrixProposalSummary summary, long revision,
    int compared, int todayOnly, int proposalOnly, IReadOnlyList<ProposalMove> moves)
{
    /// <summary>Today's words, which <see cref="ProposalMove.Indices"/> point into.</summary>
    public IReadOnlyList<AssessmentRowFacts> Today { get; } = today;

    /// <summary>The Proposal side the moves were found against.</summary>
    public MatrixProposalSummary Summary { get; } = summary;

    /// <summary>The load of today's words they were found over.</summary>
    public long Revision { get; } = revision;

    /// <summary>Words both sides have, whether or not they moved.</summary>
    public int Compared { get; } = compared;

    /// <summary>Words only today's matrix has.</summary>
    public int TodayOnly { get; } = todayOnly;

    /// <summary>Words only the Proposal's try measured.</summary>
    public int ProposalOnly { get; } = proposalOnly;

    /// <summary>Every group of words that changed cell; words that stayed put are only counted in <see cref="Compared"/>.</summary>
    public IReadOnlyList<ProposalMove> Moves { get; } = moves;

    /// <summary>Compares recorded classifications while preserving each word's evidence identity.</summary>
    public static ProposalMoveSet Of(IReadOnlyList<AssessmentRowFacts> today, MatrixProposalSummary summary)
    {
        var aligned = new byte[today.Count];
        var counts = new Dictionary<(byte From, byte To), int>();
        var compared = 0;
        for (var index = 0; index < today.Count; index++)
        {
            var word = today[index];
            aligned[index] = summary.Index.TryFind(word.Word, out var proposed) ? proposed.Cell : ProposalWordIndex.NoCell;
            if (aligned[index] == ProposalWordIndex.NoCell) continue;
            compared++;
            var from = ProposalWordIndex.Pack(word.Row, word.Column);
            if (from == aligned[index]) continue;
            var key = (from, aligned[index]);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
        var groups = counts.ToDictionary(count => count.Key, count => new int[count.Value]);
        var filled = counts.ToDictionary(count => count.Key, _ => 0);
        for (var index = 0; index < today.Count; index++)
        {
            if (aligned[index] == ProposalWordIndex.NoCell) continue;
            var key = (ProposalWordIndex.Pack(today[index].Row, today[index].Column), aligned[index]);
            if (groups.TryGetValue(key, out var indices)) indices[filled[key]++] = index;
        }
        return new ProposalMoveSet(today, summary, 0, compared, today.Count - compared,
            summary.Index.SpellingsNotIn(today.Select(word => word.Word)),
            groups.Select(group => new ProposalMove(group.Key.From, group.Key.To, group.Value)).ToArray());
    }
}
