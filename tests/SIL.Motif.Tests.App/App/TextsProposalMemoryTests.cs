using System.Runtime.CompilerServices;
using System.Diagnostics;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
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

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void AStoredTrialIndexLooksUpASixHundredEightyEightThousandWordSelection()
    {
        var trial = Trial();
        var summary = MatrixProposalSummary.Of(trial);
        var before = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var found = trial.Count(word => summary.Index.TryFind(word.Word, out _));
        output.WriteLine($"MEASURE words={Words} lookupMs={watch.ElapsedMilliseconds} lookupAllocated={GC.GetTotalAllocatedBytes(true) - before}");
        Assert.Equal(Words, found);
        Assert.Equal(Words, summary.Index.Count);
    }
}
