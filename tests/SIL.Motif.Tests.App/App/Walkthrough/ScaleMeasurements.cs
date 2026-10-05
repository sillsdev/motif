using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App.Walkthrough;

internal sealed class ScaleMeasurements(ITestOutputHelper output)
{
    private readonly List<string> _failures = [];
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
        var managedLimit = managedMiB * 1048576L;
        var processMemoryLimit = processMemoryMiB * 1048576L;
        using var macOsMemory = OperatingSystem.IsMacOS() ? ProcessMemoryDiagnostics.CreateSampler() : null;
        var processMemoryMeasure = OperatingSystem.IsMacOS() ? "physical footprint" : "RSS";
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
                timeBudgetSeconds = seconds, managedBudgetBytes = managedLimit, processMemoryBudgetBytes = processMemoryLimit,
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
                $"{seconds:F0} s / {managedMiB} MiB managed / {processMemoryMiB} MiB {processMemoryMeasure} budget");
            output.WriteLine(row);
            if (clock.Elapsed.TotalSeconds > seconds || managed > managedLimit || processMemory > processMemoryLimit)
                _failures.Add(row);
        }
    }

    public Task MeasureAsync(string step, double seconds, Action action) =>
        MeasureAsync(step, seconds, () => { action(); return Task.CompletedTask; });

    public void AssertBudgets() => Assert.True(_failures.Count == 0, string.Join(Environment.NewLine, _failures));

    public void Checkpoint(string stage)
    {
        if (!OperatingSystem.IsMacOS()) return;
        output.WriteLine(ProcessMemoryDiagnostics.FormatMacCheckpoint(stage));
    }
}
