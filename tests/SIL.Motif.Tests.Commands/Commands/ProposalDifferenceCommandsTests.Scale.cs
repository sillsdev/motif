using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

// Prints the memory and store a Difference takes at MOTIF_DIFFERENCE_SCALE_WORDS words.
public sealed partial class ProposalDifferenceCommandsTests
{
    private sealed class ScaleFactAttribute : FactAttribute
    {
        public ScaleFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("MOTIF_SLOW_SCALE") != "1")
                Skip = "Set MOTIF_SLOW_SCALE=1 to run representative-scale measurements.";
        }
    }

    [ScaleFact]
    [Trait("Category", "SlowScale")]
    public void ProducingADifferenceOverALargeWordListReportsMemoryAndRows()
    {
        var words = int.Parse(Environment.GetEnvironmentVariable("MOTIF_DIFFERENCE_SCALE_WORDS") ?? "688000",
            System.Globalization.CultureInfo.InvariantCulture);
        var mode = Environment.GetEnvironmentVariable("MOTIF_DIFFERENCE_SCALE_MODE") ?? "uniform";
        var zipf = mode is "zipf" or "wide";
        using var fixture = new Fixture(_root, extraWords: words, variedExtraWords: mode != "uniform",
            limitedExtraWords: mode == "wide",
            placesOfExtraWord: zipf ? i => Math.Max(1, (int)(words * 0.1 / (i + 1))) : null);
        Console.WriteLine($"DIFFERENCE PRODUCE {mode} fixture of {words:N0} words ready");
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        var baseline = Process.GetCurrentProcess().WorkingSet64;
        var baselineManaged = GC.GetTotalMemory(true);
        var storeBefore = fixture.DatabaseBytes();
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var sampler = new PeakSampler();

        var produced = ProposalDifferenceCommands.Produce(fixture.ProduceRequest());

        var peak = sampler.Stop();
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        Assert.True(produced.Succeeded, produced.Refusal?.Message);
        var summary = produced.Value!.Summary;
        var rows = fixture.Rows(summary.DifferenceId);
        var report = string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"DIFFERENCE PRODUCE | {mode} | {words:N0} extra words | compared {summary.Words:N0} words, {summary.Occurrences:N0} occurrences | " +
            $"groups {summary.Matrix.Count(cell => cell.WordsListed > 0) + summary.Buckets.Count(bucket => bucket.WordsListed > 0) + summary.Limitations.Count(limit => limit.WordsListed > 0)} | " +
            $"listed {summary.ListedWordCount:N0} | rows cases {rows.Cases:N0} occurrences {rows.Occurrences:N0} buckets {rows.Buckets:N0} | " +
            $"peak working set +{(peak.WorkingSet - baseline) / 1048576d:F0} MiB, peak managed +{(peak.Managed - baselineManaged) / 1048576d:F0} MiB, " +
            $"allocated {allocated / 1048576d:F0} MiB | Difference rows add {(fixture.DatabaseBytes() - storeBefore) / 1048576d:F1} MiB to the store, word facts {fixture.FactsBytes(summary.DifferenceId) / 1048576d:F1} MiB | summary JSON {fixture.SummaryBytes(summary.DifferenceId):N0} bytes");
        Console.WriteLine(report);
    }

    private sealed class PeakSampler
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _task;
        private long _workingSet;
        private long _managed;

        public PeakSampler() => _task = Task.Run(() =>
        {
            using var process = Process.GetCurrentProcess();
            while (!_stop.IsCancellationRequested)
            {
                process.Refresh();
                _workingSet = Math.Max(_workingSet, process.WorkingSet64);
                _managed = Math.Max(_managed, GC.GetTotalMemory(false));
                Thread.Sleep(20);
            }
        });

        public (long WorkingSet, long Managed) Stop()
        {
            _stop.Cancel();
            _task.Wait();
            return (_workingSet, _managed);
        }
    }
}
