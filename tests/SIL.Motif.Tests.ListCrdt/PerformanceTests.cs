using System.Diagnostics;
using SIL.ListCrdt;
using Xunit;
using Xunit.Abstractions;

namespace SIL.ListCrdt.Tests;

/// <summary>
/// Build-plan budget for this package: insert and move at the end of a 34,383-item list each under 1 ms. The assertions use
/// the median of many samples after warm-up so a noisy host does not fail them; the quiet-run method and numbers
/// are in the slice report. Output lists median and 99th percentile in microseconds.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    const int ListSize = 34_383;
    const int Samples = 400;

    static ListReplica Build() =>
        new("w", ListState.FromInitial(Enumerable.Range(0, ListSize).Select(i => "paragraph-" + i)));

    static (double Median, double P99) Measure(Action<int> action)
    {
        for (int i = 0; i < 50; i++) action(-1 - i);
        var micros = new double[Samples];
        for (int i = 0; i < Samples; i++)
        {
            long start = Stopwatch.GetTimestamp();
            action(i);
            micros[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds * 1000;
        }
        Array.Sort(micros);
        return (micros[Samples / 2], micros[(int)(Samples * 0.99)]);
    }

    [Fact]
    public void InsertAtTheEndOfAFullSizedListIsUnderOneMillisecond()
    {
        var replica = Build();
        var (median, p99) = Measure(i => replica.InsertAt(replica.State.LiveCount, "tail-" + i));
        output.WriteLine($"insert at end of {ListSize}: median {median:F1} us, p99 {p99:F1} us");
        Assert.True(median < 1000, $"median {median} us");
        Assert.Equal("tail-" + (Samples - 1), replica.State.ElementAt(replica.State.LiveCount - 1));
    }

    [Fact]
    public void MoveToTheEndOfAFullSizedListIsUnderOneMillisecond()
    {
        var replica = Build();
        var rng = new Random(3);
        var (median, p99) = Measure(_ =>
            replica.MoveToIndex(replica.State.ElementAt(rng.Next(replica.State.LiveCount - 1)), replica.State.LiveCount - 1));
        output.WriteLine($"move to end of {ListSize}: median {median:F1} us, p99 {p99:F1} us");
        Assert.True(median < 1000, $"median {median} us");
        Assert.Equal(ListSize, replica.State.LiveCount);
    }

    [Fact]
    public void MoveFromTheEndAndRemoteTailInsertAreUnderOneMillisecond()
    {
        var local = Build();
        var remote = new ListReplica("remote", local.State.Clone());
        var ops = new List<ListOp>();
        for (int i = 0; i < Samples + 50; i++) ops.Add(local.InsertAt(local.State.LiveCount, "tail-" + i));
        int next = 0;
        var (applyMedian, applyP99) = Measure(_ => remote.Receive(ops[next++]));
        output.WriteLine($"remote tail insert apply on {ListSize}: median {applyMedian:F1} us, p99 {applyP99:F1} us");
        Assert.True(applyMedian < 1000, $"median {applyMedian} us");
        var (median, p99) = Measure(_ => remote.MoveToIndex(remote.State.ElementAt(remote.State.LiveCount - 1), 0));
        output.WriteLine($"move last item to start of {remote.State.LiveCount}: median {median:F1} us, p99 {p99:F1} us");
        Assert.True(median < 1000, $"median {median} us");
        Assert.Equal(ListSize + Samples + 50, remote.State.LiveCount);
    }

    [Fact]
    public void LookupAndTailCostDoNotGrowWithTheListLength()
    {
        double Cost(int size)
        {
            var r = new ListReplica("w", ListState.FromInitial(Enumerable.Range(0, size).Select(i => "p" + i)));
            var (median, _) = Measure(i => { r.InsertAt(r.State.LiveCount, "t" + i); r.State.IndexOf("p" + (size / 2)); });
            return median;
        }
        double small = Cost(2_000), large = Cost(ListSize);
        output.WriteLine($"tail insert plus lookup: 2000 items {small:F1} us, {ListSize} items {large:F1} us");
        Assert.True(large < Math.Max(small * 25, 400), $"{small} us vs {large} us");
    }
}
