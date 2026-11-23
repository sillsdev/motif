using SIL.Motif.Contract.HandAnalysis;
using System.Diagnostics;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.RecordStore.HandAnalyses;

/// <summary>
/// Cost of the first pin and warm reads, one process, Stopwatch around each public call. Each result also goes to
/// a per-process file derived from MOTIF_RECORD_STORE_MEASURE_LOG when set.
/// </summary>
internal static class HandAnalysisMeasurement
{
    public static async Task Run(string label, ImportedProject project, ITestOutputHelper output, int expectedWordforms, int lookups)
    {
        var log = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_LOG");
        void Say(string line)
        {
            line = $"HAND {label} {line}";
            output.WriteLine(line);
            if (log is not null) File.AppendAllText($"{log}.{Environment.ProcessId}", line + Environment.NewLine);
        }
        var cache = project.Cache!;
        var expected = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().Count;
        var expectedAnalyses = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().Count;
        var expectedBundles = cache.ServiceLocator.GetInstance<IWfiMorphBundleRepository>().Count;
        Assert.Equal(expectedWordforms, expected);
        if (label == "Cebuano") Assert.Equal(0, expectedAnalyses);
        var entries = cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances().Take(100).Select(e => e.Guid).ToArray();
        project.ReleaseCache();
        var basis = new ExternalBasis("source", "parser", "settings");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var start = Stopwatch.GetTimestamp();
        double Lap() { var now = Stopwatch.GetTimestamp(); var ms = Stopwatch.GetElapsedTime(start, now).TotalMilliseconds; start = now; return ms; }
        await using var host = await HandAnalysisHost.OpenAsync(project.Store);
        Say($"open={Lap():F1} ms");
        await host.CheckPointAsync();
        Say($"first pin, verification (store acquisition)={Lap():F1} ms");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetTotalMemory(true);
        start = Stopwatch.GetTimestamp();
        await using var pin = await host.PinAsync(basis, null);
        Say($"first pin, state read + projection build={Lap():F1} ms");
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var after = GC.GetTotalMemory(true);
        Say($"managed memory before projection={before / 1048576.0:F1} MiB; after={after / 1048576.0:F1} MiB; projection delta={(after - before) / 1024.0:F0} KiB");
        start = Stopwatch.GetTimestamp();
        await using var warm = await host.PinAsync(basis, null);
        Say($"warm pin (retained point)={Lap():F1} ms");
        await using var other = await host.PinAsync(basis with { ParserRevision = "other" }, null);
        Say($"warm pin, new basis (projection reused)={Lap():F1} ms");
        var all = await Pages.All(p => pin.Inventory(p), 200);
        Assert.Equal(expected, all.Count);
        Assert.Equal(expectedAnalyses, pin.AnalysisCount);
        Assert.Equal(expectedBundles, pin.BundleCount);
        var random = new Random(7);
        var keys = Enumerable.Range(0, lookups).Select(_ => all[random.Next(all.Count)])
            .Select(h => (Header: h, Key: TestWord.Key(h.Forms.Keys.First(), h.Forms.Values.First()))).ToArray();
        var times = new double[keys.Length];
        for (var i = 0; i < keys.Length; i++)
        {
            var t = Stopwatch.GetTimestamp();
            var page = await pin.Words(new WordQuery.Identity(keys[i].Key), new PageRequest());
            times[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            Assert.Contains(keys[i].Header.Id, page.Items.Cast<WordFact.Header>().SelectMany(h => h.ExactMatches));
        }
        Array.Sort(times);
        Say($"warm Identity lookup over {keys.Length} random keys: p50={times[times.Length / 2]:F3} ms p95={times[(int)(times.Length * 0.95)]:F3} ms max={times[^1]:F3} ms");
        var costs = new double[entries.Length];
        for (var i = 0; i < entries.Length; i++)
        {
            var t = Stopwatch.GetTimestamp();
            _ = pin.PreviewDeletion(entries[i]);
            costs[i] = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        }
        Array.Sort(costs);
        Say($"warm PreviewDeletion over {costs.Length} entries: p50={costs[costs.Length / 2]:F3} ms p95={costs[(int)(costs.Length * 0.95)]:F3} ms max={costs[^1]:F3} ms");
        Say($"wordforms={all.Count}; analyses={pin.AnalysisCount}; bundles={pin.BundleCount}; stamp verification={pin.Stamp.Verification}");
    }
}

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisIndonesianMeasurementTests(ImportedProject project, ITestOutputHelper output) : IClassFixture<ImportedProject>
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public Task IndonesianCostOfFirstPinAndWarmReads() =>
        HandAnalysisMeasurement.Run("Indonesian", project, output, project.Cache!.ServiceLocator.GetInstance<IWfiWordformRepository>().Count, 1000);
}

/// <summary>The phase timings of loading a point through the host, on a private copy of an already imported Cebuano store.</summary>
public sealed class HandAnalysisHostPhaseMeasurementTests(ITestOutputHelper output)
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_CEBUANO_STORE")]
    public async Task CebuanoStatusPhaseTimings()
    {
        var source = Path.GetFullPath(Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_CEBUANO_STORE")!);
        var root = Directory.CreateTempSubdirectory("motif-host-phases-").FullName;
        try
        {
            foreach (var file in Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(source)!, Path.GetFileName(source) + "*"))
                if (File.Exists(file)) File.Copy(file, Path.Combine(root, Path.GetFileName(file)));
            var store = Path.Combine(root, Path.GetFileName(source));
            var log = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_LOG");
            void Say(string line)
            {
                line = "HAND host-phases " + line;
                output.WriteLine(line);
                if (log is not null) File.AppendAllText($"{log}.{Environment.ProcessId}", line + Environment.NewLine);
            }
            Say("load average=" + File.ReadAllText("/proc/loadavg").Trim());
            var watch = Stopwatch.StartNew();
            await using var host = await HandAnalysisHost.OpenAsync(store, new ExternalBasis("s", "p", "t"), loadPoint: true);
            Say($"open + load point={watch.Elapsed.TotalMilliseconds:F0} ms");
            var status = await host.StatusAsync();
            foreach (var phase in status.Phases) Say($"phase {phase.Phase} start={phase.Start.TotalMilliseconds:F0} ms duration={phase.Duration.TotalMilliseconds:F0} ms");
            Say($"readiness={status.Readiness} index={status.ReferenceIndex} verification={status.Current!.Verification}");
            foreach (var table in status.Tables) Say($"table {table.Table}={table.State} ({table.Detail})");
            watch.Restart();
            await using var warm = await host.PinAsync(new ExternalBasis("s", "p", "t"), null);
            Say($"warm pin={watch.Elapsed.TotalMilliseconds:F1} ms");
            Say("load average after=" + File.ReadAllText("/proc/loadavg").Trim());
        }
        finally { Directory.Delete(root, true); }
    }
}

[Collection(LcmCacheTestCollection.Name)]
public sealed class HandAnalysisCebuanoTests(ImportedCebuano project, ITestOutputHelper output) : IClassFixture<ImportedCebuano>
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_CEBUANO")]
    public Task CebuanoHoldsFortyFourThousandWordformsAndNoAnalysesAndPinsWithinBudget() =>
        HandAnalysisMeasurement.Run("Cebuano", project, output, 44163, 1000);
}
