using System.Diagnostics;
using System.Runtime.CompilerServices;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that What Changed's moves over 688,000 words are positions and counts, a word's view model is made only for a
/// move that is opened, and nothing of them stays once the Proposal's side is dropped. It prints what finding the
/// moves costs, which depends on the machine and so is not asserted.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsProposalChangesMemoryTests(ITestOutputHelper output)
{
    private const int Words = 688_000;

    // Every third word does not parse today; every fifth does not with the Proposal, so words move both ways.
    private static AssessmentWordResult[] Trial(int every) => [.. Enumerable.Range(0, Words).Select(index =>
        new AssessmentWordResult("word" + index, index % every == 0 ? "unassessed" : "no-analysis", false, "Complete", 1, null))];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (CompareViewModel Compare, DifferenceViewModel Changes, long Baseline, long Held, int Moved) FindAndShowTheMoves()
    {
        var today = Trial(3);
        var summary = MatrixProposalSummary.Of(Trial(5));
        var compare = new CompareViewModel();
        compare.LoadSources(today);
        compare.ShowProposal(MarkupView.TodayToProposal, MatrixSideState.Held, summary);
        today = null!;
        GC.Collect();
        var baseline = GC.GetTotalMemory(true);
        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();

        var moves = compare.ProposalMoves()!;
        var found = watch.ElapsedMilliseconds;
        var foundAllocated = GC.GetTotalAllocatedBytes(true) - allocated;
        var foundHeld = GC.GetTotalMemory(true) - baseline;

        var changes = new DifferenceViewModel();
        watch.Restart();
        changes.LoadProposalMoves(moves);
        var shownMs = watch.ElapsedMilliseconds;
        var shownHeld = GC.GetTotalMemory(true) - baseline;

        watch.Restart();
        var biggest = changes.Moves.MaxBy(move => move.Count)!;
        var listed = biggest.Words.Count;
        var listedMs = watch.ElapsedMilliseconds;
        var listedHeld = GC.GetTotalMemory(true) - baseline;
        output.WriteLine($"MEASURE words={Words} moves={moves.Moves.Count} moved={changes.MovedCount} findMs={found} " +
            $"findAllocated={foundAllocated} findHeld={foundHeld} loadMs={shownMs} loadHeld={shownHeld} " +
            $"biggestWords={listed} openMs={listedMs} openHeld={listedHeld}");
        Assert.Equal(1, compare.MoveSets);
        Assert.Equal(0, ((LazyMovedWords)biggest.Words).Made);
        Assert.True(listedHeld - shownHeld < 8L << 20, $"opening the biggest move held {listedHeld - shownHeld} bytes");
        Assert.True(foundHeld < moves.Moves.Sum(move => move.Indices.Length) * 6L, $"the moves held {foundHeld} bytes");
        return (compare, changes, baseline, foundHeld, changes.MovedCount);
    }

    [Fact]
    public void ASixHundredEightyEightThousandWordProposalFindsItsMovesAsPositionsAndLetsAllOfThemGo()
    {
        var (compare, changes, baseline, held, moved) = FindAndShowTheMoves();
        Assert.InRange(moved, Words / 6, Words / 2);
        Assert.InRange(held, 1, moved * 6L);

        compare.ShowProposal(MarkupView.Today, MatrixSideState.None, null);
        changes.LoadProposalMoves(null);
        for (var pass = 0; pass < 3; pass++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        var after = GC.GetTotalMemory(true) - baseline;
        output.WriteLine($"MEASURE words={Words} heldAfterRelease={after}");

        Assert.Empty(changes.Moves);
        Assert.Null(compare.HeldProposalMoves);
        Assert.True(after <= 1L << 20, $"{after} bytes remain above today's own words");
        GC.KeepAlive(compare);
    }
}
