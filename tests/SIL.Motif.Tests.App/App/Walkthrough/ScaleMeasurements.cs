using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class ScaleMeasurements(ITestOutputHelper output)
{
    private const int TrendCeilingMultiplier = 3;
    private readonly List<string> _failures = [];
    private readonly Dictionary<string, ScaleRepositoryReadCount> _readCounts = new(StringComparer.Ordinal);
    private readonly string? _metricsPath = CreateMetricsPath();

    private static string? CreateMetricsPath()
    {
        if (Environment.GetEnvironmentVariable("MOTIF_SCALE_METRICS_ROOT") is not { Length: > 0 } root) return null;
        Directory.CreateDirectory(root);
        return Path.Combine(root, $"scale-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
    }

    public async Task MeasureAsync(string step, double seconds, Func<Task> action,
        int managedMiB = 1536, int processMemoryMiB = 1536)
    {
        var timeLimitSeconds = seconds * TrendCeilingMultiplier;
        var managedLimitMiB = managedMiB * TrendCeilingMultiplier;
        var processMemoryLimitMiB = processMemoryMiB * TrendCeilingMultiplier;
        var managedLimit = managedLimitMiB * 1048576L;
        var processMemoryLimit = processMemoryLimitMiB * 1048576L;
        using var macOsMemory = OperatingSystem.IsMacOS() ? ProcessMemoryDiagnostics.CreateSampler() : null;
        var processMemoryMeasure = OperatingSystem.IsMacOS() ? "physical footprint" : "RSS";
        using var readObservation = ScaleCountHarness.ObserveRepositoryReads();
        using var process = Process.GetCurrentProcess();
        using var cancellation = new CancellationTokenSource();
        long managed = 0;
        long processMemory = 0;
        var clock = Stopwatch.StartNew();
        var lastRecord = TimeSpan.Zero;
        void Record(bool completed)
        {
            if (_metricsPath is null) return;
            File.AppendAllText(_metricsPath, JsonSerializer.Serialize(new
            {
                step, completed, processId = Environment.ProcessId, elapsedSeconds = clock.Elapsed.TotalSeconds,
                peakManagedBytes = managed, peakProcessMemoryBytes = processMemory,
                processMemoryMeasure = OperatingSystem.IsMacOS() ? "physical footprint" : "rss",
                timeBudgetSeconds = timeLimitSeconds, managedBudgetBytes = managedLimit,
                processMemoryBudgetBytes = processMemoryLimit,
            }) + Environment.NewLine);
            lastRecord = clock.Elapsed;
        }
        void Sample()
        {
            process.Refresh();
            managed = Math.Max(managed, GC.GetTotalMemory(false));
            var currentProcessMemory = OperatingSystem.IsMacOS()
                ? macOsMemory!.ReadSnapshot().PhysicalFootprint
                : process.WorkingSet64;
            processMemory = Math.Max(processMemory, currentProcessMemory);
        }
        Sample();
        Record(false);
        var sampler = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(20, cancellation.Token);
                    Sample();
                    if (clock.Elapsed - lastRecord >= TimeSpan.FromSeconds(1)) Record(false);
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        });
        try { await action(); }
        finally
        {
            clock.Stop();
            cancellation.Cancel();
            await sampler;
            Sample();
            Record(true);
            var row = string.Create(CultureInfo.InvariantCulture,
                $"SCALE | {step} | {clock.Elapsed.TotalSeconds:F3} s | {managed / 1048576d:F1} MiB managed | " +
                $"{processMemory / 1048576d:F1} MiB {processMemoryMeasure} | " +
                $"{timeLimitSeconds:F0} s / {managedLimitMiB} MiB managed / " +
                $"{processMemoryLimitMiB} MiB {processMemoryMeasure} ceiling");
            output.WriteLine(row);
            var reads = readObservation.Snapshot();
            _readCounts[step] = reads;
            output.WriteLine($"SCALE READ | {step} | {reads.Queries} queries | {reads.RecordsDeserialized} records");
            if (clock.Elapsed.TotalSeconds > timeLimitSeconds || managed > managedLimit || processMemory > processMemoryLimit)
                _failures.Add(row);
        }
    }

    public Task MeasureAsync(string step, double seconds, Action action) =>
        MeasureAsync(step, seconds, () => { action(); return Task.CompletedTask; });

    public void AssertReadCounts(string step,
        long minimumQueries, long maximumQueries, long minimumRecords, long maximumRecords)
    {
        if (!_readCounts.TryGetValue(step, out var counts))
            throw new InvalidOperationException($"No read counts were recorded for {step}.");
        Assert.InRange(counts.Queries, minimumQueries, maximumQueries);
        Assert.InRange(counts.RecordsDeserialized, minimumRecords, maximumRecords);
    }

    public void AssertBudgets() => Assert.True(_failures.Count == 0, string.Join(Environment.NewLine, _failures));

    public void Checkpoint(string stage)
    {
        if (!OperatingSystem.IsMacOS()) return;
        output.WriteLine(ProcessMemoryDiagnostics.FormatMacCheckpoint(stage));
    }
}
