using System.Runtime.CompilerServices;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Contract.Responses;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the Proposal's side of the matrix stays a few counts however many words the Trial measured, and prints
/// what reducing it allocates and the heap it adds, which depend on the machine and so are not asserted.
/// </summary>
public sealed class TextsProposalMemoryTests(ITestOutputHelper output)
{
    private const int Words = 688_000;

    private static AssessmentWordResult[] Trial() => [.. Enumerable.Range(0, Words).Select(index =>
        new AssessmentWordResult("word" + index, index % 3 == 0 ? "unassessed" : "no-analysis", false, "Complete", 1, null))];

    // Reduces a fresh Trial and lets go of it, handing back only what the page would hold.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<(MatrixProposalSummary Summary, WeakReference Rows)> ReduceAsync()
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
        return (summary, new WeakReference(trial));
    }

    [Fact]
    public async Task ASixHundredEightyEightThousandWordTrialIsHeldAsAFewCountsAndADigestWithoutItsRows()
    {
        var (summary, rows) = await ReduceAsync();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(rows.IsAlive);
        Assert.Equal(Words, summary.Words.Count);
        Assert.InRange(summary.Cells.Count, 1, 40);
        GC.KeepAlive(summary);
    }
}
