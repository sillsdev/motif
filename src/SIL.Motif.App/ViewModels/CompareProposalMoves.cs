using SIL.Motif.Commands.Queries;

namespace SIL.Motif.App.ViewModels;

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
}

public sealed partial class CompareViewModel
{
    private ProposalMoveSet? _moves;

    /// <summary>Counts the move sets made, so a test can see that a flip or a second look made none.</summary>
    internal int MoveSets { get; private set; }

    /// <summary>The moves made for the held side and today's words, or null when none were made or they are out of date.</summary>
    internal ProposalMoveSet? HeldProposalMoves => HoldsProposalSide ? _moves : null;

    /// <summary>Finds where the held Proposal side moves today's words, once for that side and load; null without a side.</summary>
    internal ProposalMoveSet? ProposalMoves()
    {
        if (!HoldsProposalSide) return null;
        if (_moves is not null) return _moves;
        var summary = _proposalSummary!;
        var aligned = AlignedProposalCells();
        var counts = new Dictionary<int, int>();
        var compared = 0;
        for (var index = 0; index < aligned.Length; index++)
        {
            if (aligned[index] == ProposalWordIndex.NoCell) continue;
            compared++;
            var from = ProposalWordIndex.Pack(_all[index].Row, _all[index].Column);
            if (from != aligned[index]) counts[KeyOf(from, aligned[index])] = counts.GetValueOrDefault(KeyOf(from, aligned[index])) + 1;
        }
        var groups = counts.ToDictionary(count => count.Key, count => new int[count.Value]);
        var filled = counts.ToDictionary(count => count.Key, _ => 0);
        for (var index = 0; index < aligned.Length; index++)
        {
            if (aligned[index] == ProposalWordIndex.NoCell) continue;
            var key = KeyOf(ProposalWordIndex.Pack(_all[index].Row, _all[index].Column), aligned[index]);
            if (groups.TryGetValue(key, out var indices)) indices[filled[key]++] = index;
        }
        MoveSets++;
        return _moves = new ProposalMoveSet(_all, summary, _wordsRevision, compared, _all.Count - compared,
            summary.Index.SpellingsNotIn(_all.Select(word => word.Word)),
            groups.Select(group => new ProposalMove((byte)(group.Key >> 8), (byte)(group.Key & 0xFF), group.Value)).ToArray());
    }

    private static int KeyOf(byte from, byte to) => from << 8 | to;
}
