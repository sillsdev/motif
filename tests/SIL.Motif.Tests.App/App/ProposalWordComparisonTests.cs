using System.Runtime.CompilerServices;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ProposalWordComparisonTests
{
    private static AssessmentWordResult Word(string form, bool parsed = false) =>
        new(form, parsed ? "unassessed" : "no-analysis", false, "Complete", 1, null);

    [Theory]
    [InlineData("a,a,b", "a,b,c", 1)]
    [InlineData("a,a", "a", 0)]
    [InlineData("a,b", "a,a,c,c", 1)]
    public void ProposalOnlyWordsAreCountedByIdentityRatherThanTotals(string today, string proposal, int only)
    {
        var facts = today.Split(',').Select(form => AssessmentRowFacts.Counted(Word(form), null, false)).ToArray();
        var summary = MatrixProposalSummary.Of([.. proposal.Split(',').Select(form => Word(form))]);
        Assert.Equal(only, ProposalMoveSet.Of(facts, summary).ProposalOnly);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DifferenceViewModel Changes, WeakReference Moves) MakeAndDropMoves()
    {
        var facts = new[] { Word("a"), Word("b"), Word("c", true) }
            .Select(word => AssessmentRowFacts.Counted(word, null, false)).ToArray();
        var summary = MatrixProposalSummary.Of([Word("a"), Word("b", true), Word("c", true)]);
        var set = ProposalMoveSet.Of(facts, summary);
        var changes = new DifferenceViewModel();
        changes.LoadProposalMoves(set);
        var moves = new WeakReference(set);
        changes.LoadProposalMoves(null);
        return (changes, moves);
    }

    [Fact]
    public void DroppingTheProposalSideReleasesItsMoves()
    {
        var (changes, moves) = MakeAndDropMoves();
        for (var pass = 0; pass < 3; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(moves.IsAlive);
        Assert.Null(changes.ProposalMoves);
        GC.KeepAlive(changes);
    }
}
