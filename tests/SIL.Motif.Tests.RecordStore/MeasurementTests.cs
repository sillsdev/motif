using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>
/// Manual per-call cost measurement. Set MOTIF_RECORD_STORE_MEASURE to an external .fwdata copy and optionally
/// MOTIF_RECORD_STORE_MEASURE_DIR to a directory that keeps the imported store between runs (reused when present).
/// MOTIF_RECORD_STORE_MEASURE_REPS (default 4) bounds the repeated calls, and each result is also appended to
/// MOTIF_RECORD_STORE_MEASURE_LOG when set, so a slow run keeps its partial results.
/// Method: wall clock (Stopwatch) around each public call on one handle, then one cold handle; edits toggle a
/// LexEntry homograph number and restore it.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class MeasurementTests(ITestOutputHelper output)
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_MEASURE")]
    public async Task PerCallCostOnPrivateCopy()
    {
        var source = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE")!;
        var kept = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_DIR");
        var root = kept ?? Path.Combine(Path.GetTempPath(), "motif-measure-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var database = Path.Combine(root, "measure.sqlite");
        var reps = int.TryParse(Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_REPS"), out var configured) ? configured : 4;
        var log = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_LOG");
        void Say(string line)
        {
            output.WriteLine(line);
            if (log is not null) File.AppendAllText(log, line + Environment.NewLine);
        }
        try
        {
            if (!File.Exists(database))
            {
                var watch0 = Stopwatch.StartNew();
                using var cache = new FwDataProjectLoader().LoadScratchCache(source, Path.Combine(root, "templates"));
                await LcmRecordBridge.ImportAsync(cache, source, database);
                Say($"MEASURE import={watch0.Elapsed.TotalMilliseconds:F0} ms");
            }
            var watch = Stopwatch.StartNew();
            double Lap() { var value = watch.Elapsed.TotalMilliseconds; watch.Restart(); return value; }
            await using var store = new Store(database);
            Say($"MEASURE open={Lap():F1} ms");
            var references = await store.ReferencesAsync();
            Say($"MEASURE first ReferencesAsync={Lap():F1} ms");
            var records = await store.ReadAsync();
            Say($"MEASURE ReadAsync={Lap():F1} ms");
            var entry = records.First(r => r.ClassId == LexEntryTags.kClassId);
            var key = LexEntryTags.kflidHomographNumber.ToString();
            var current = entry.Values[key];
            var revision = references.Revision;
            watch.Restart();
            for (var n = 0; n < Math.Min(reps, 3); n++)
            {
                try { await store.PrepareDeletionAsync(entry.Id, revision, null); }
                catch (IncompleteDeletionClosureException) { }
                Say($"MEASURE PrepareDeletionAsync warm #{n}={Lap():F1} ms");
            }
            for (var n = 0; n < reps; n++)
            {
                var next = JsonSerializer.SerializeToElement(n % 2 == 0 ? current.GetInt32() + 1 : current.GetInt32());
                var prior = n % 2 == 0 ? current : JsonSerializer.SerializeToElement(current.GetInt32() + 1);
                revision = await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, prior, next, revision, Guid.NewGuid());
                Say($"MEASURE SetFieldAsync #{n}={Lap():F1} ms");
            }
            await store.DisposeAsync();
            watch.Restart();
            await using var cold = new Store(database);
            Say($"MEASURE cold open={Lap():F1} ms");
            try { await cold.PrepareDeletionAsync(entry.Id, revision, null); } catch (IncompleteDeletionClosureException) { }
            Say($"MEASURE cold PrepareDeletionAsync={Lap():F1} ms");
            var cold2 = JsonSerializer.SerializeToElement(current.GetInt32() + 1);
            revision = await cold.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, current, cold2, revision, Guid.NewGuid());
            Say($"MEASURE cold SetFieldAsync={Lap():F1} ms");
            revision = await cold.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, cold2, current, revision, Guid.NewGuid());
            Say($"MEASURE warm-after-cold SetFieldAsync={Lap():F1} ms");
        }
        finally { if (kept is null && Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
