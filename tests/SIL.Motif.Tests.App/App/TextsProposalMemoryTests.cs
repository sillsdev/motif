using System.Runtime.CompilerServices;
using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the Proposal's side of the matrix stays a few counts and some values a word however many words the
/// Trial measured and never keeps a Trial row alive, and that a flip of the view aligns it once. It prints what
/// reducing and aligning cost and allocate, which depend on the machine and so are not asserted.
/// </summary>
public sealed class TextsProposalMemoryTests(ITestOutputHelper output)
{
    private const int Words = 688_000;

    private static AssessmentWordResult[] Trial() => [.. Enumerable.Range(0, Words).Select(index =>
        new AssessmentWordResult("word" + index, index % 3 == 0 ? "unassessed" : "no-analysis", false, "Complete", 1, null))];

    // Reduces a fresh Trial and lets go of it, handing back only what the page would hold.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(MatrixProposalSummary Summary, WeakReference Rows, WeakReference Row)> ReduceAsync()
    {
        var trial = Trial();
        GC.Collect();
        var before = GC.GetTotalMemory(true);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var peak = before;
        using var sampling = new CancellationTokenSource();
        var sampler = Task.Run(() =>
        {
            while (!sampling.IsCancellationRequested) { peak = Math.Max(peak, GC.GetTotalMemory(false)); Thread.Sleep(1); }
        });

        var summary = MatrixProposalSummary.Of(trial);

        await sampling.CancelAsync();
        await sampler;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        output.WriteLine($"MEASURE words={Words} allocatedWhileReducing={allocated} peakAbove={peak - before} cells={summary.Cells.Count}");
        return (summary, new WeakReference(trial), new WeakReference(trial[Words / 2]));
    }

    [Fact]
    public async Task ASixHundredEightyEightThousandWordTrialIsHeldAsValuesAWordWithoutAnyOfItsRows()
    {
        GC.Collect();
        var baseline = GC.GetTotalMemory(true);
        var (summary, rows, row) = await ReduceAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var held = GC.GetTotalMemory(true) - baseline;
        output.WriteLine($"MEASURE words={Words} heldWithRowsReleased={held} perWord={held / (double)Words:F1}");

        Assert.False(rows.IsAlive);
        Assert.False(row.IsAlive, "a Trial row is still reachable from the Proposal's side");
        Assert.Equal(Words, summary.Words.Count);
        Assert.Equal(Words, summary.Index.Count);
        Assert.InRange(summary.Cells.Count, 1, 40);
        GC.KeepAlive(summary);
    }

    [Fact]
    public async Task AFlipOverASixHundredEightyEightThousandWordSelectionAlignsTheProposalOnceAndParsesNothing()
    {
        var trial = Trial();
        var summary = MatrixProposalSummary.Of(trial);
        var indexBefore = GC.GetTotalAllocatedBytes(true);
        var indexWatch = Stopwatch.StartNew();
        var rebuilt = ProposalWordIndex.Of(trial, new byte[Words]);
        output.WriteLine($"MEASURE indexBuildMs={indexWatch.ElapsedMilliseconds} indexAllocated={GC.GetTotalAllocatedBytes(true) - indexBefore} count={rebuilt.Count}");
        var compare = new CompareViewModel();
        compare.LoadSources(trial);
        compare.SelectCells([new(WordProjectStatus.NotPresent, CompareColumnKind.NoParse)]);
        GC.Collect();
        var heap = GC.GetTotalMemory(true);

        var sinceLoad = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        compare.ShowProposal(MarkupView.Proposal, MatrixSideState.Held, summary);
        var first = watch.ElapsedMilliseconds;
        var firstAllocated = GC.GetTotalAllocatedBytes(true) - sinceLoad;
        var work = compare.ProposalWork;
        var listed = compare.Words.Count;
        var heapAbove = GC.GetTotalMemory(true) - heap;

        sinceLoad = GC.GetTotalAllocatedBytes(true);
        watch.Restart();
        foreach (var view in new[] { MarkupView.TodayToProposal, MarkupView.Today, MarkupView.Proposal })
            compare.ShowProposal(view, MatrixSideState.Held, summary);
        var flips = watch.ElapsedMilliseconds;
        var flipsAllocated = GC.GetTotalAllocatedBytes(true) - sinceLoad;

        output.WriteLine($"MEASURE words={Words} firstProposalViewMs={first} firstAllocated={firstAllocated} " +
            $"heapAboveWhileHeld={heapAbove} threeMoreFlipsMs={flips} threeMoreFlipsAllocated={flipsAllocated} " +
            $"indexCount={summary.Index.Count} listed={listed}");
        Assert.Equal(Words, summary.Index.Count);
        Assert.Equal(2, work);
        Assert.Equal(work, compare.ProposalWork);
        Assert.Equal(WordListBasis.Proposal, compare.ListBasis);
        Assert.Equal(Words - (Words + 2) / 3, listed);
        GC.KeepAlive(trial);
        await Task.CompletedTask;
    }
}
