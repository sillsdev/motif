using System.Diagnostics;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using Xunit;
using Xunit.Abstractions;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>
/// Manual measurement of format 7 on a private copy of a format 6 store (never committed). Set MOTIF_RECORD_STORE_V6_STORE to its path
/// (its .resources folder is copied with it) and MOTIF_RECORD_STORE_MEASURE_LOG to a file that keeps the results.
/// Each run copies the store, upgrades it by its first guarded write, then reads headers and 200 distinct cold chunks through a read-only handle.
/// </summary>
public sealed class DerivedTablesMeasurementTests(ITestOutputHelper output)
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_V6_STORE")]
    public async Task UpgradeBackfillKeyedReadsAndHeadersOnAFormatSixCopy()
    {
        var source = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_V6_STORE")!;
        var log = Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_LOG");
        void Say(string line)
        {
            output.WriteLine(line);
            if (log is not null) File.AppendAllText(log, line + Environment.NewLine);
        }
        static string Load() => File.ReadAllText("/proc/loadavg").Split(' ')[0];
        var runs = int.TryParse(Environment.GetEnvironmentVariable("MOTIF_RECORD_STORE_MEASURE_REPS"), out var configured) ? configured : 3;
        for (var run = 1; run <= runs; run++)
        {
            var root = Directory.CreateTempSubdirectory("motif-v7-measure-").FullName;
            try
            {
                var copy = Path.Combine(root, "records.sqlite");
                File.Copy(source, copy);
                if (Directory.Exists(source + ".resources")) CopyDirectory(source + ".resources", copy + ".resources");
                var loadBefore = Load();
                var write = Stopwatch.StartNew();
                string backfill, upgrade;
                await using (var store = new Store(copy))
                {
                    var records = await store.ReadAsync();
                    var entry = records.First(r => r.ClassId == LexEntryTags.kClassId && r.Values.TryGetValue(LexEntryTags.kflidHomographNumber.ToString(), out var h)
                        && h.ValueKind == JsonValueKind.Number);
                    var old = entry.Values[LexEntryTags.kflidHomographNumber.ToString()];
                    var revision = (await store.ReferencesAsync()).Revision;
                    write.Restart();
                    await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, old,
                        JsonSerializer.SerializeToElement(old.GetInt32() + 1), revision, Guid.NewGuid());
                    var cost = store.LastUpgrade!;
                    backfill = $"{cost.BackfillElapsed.TotalSeconds:F2}";
                    upgrade = $"{cost.Elapsed.TotalSeconds:F2}";
                }
                var loadAfterUpgrade = Load();
                var chunks = new List<int>();
                {
                    using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={copy};Mode=ReadOnly;Pooling=False");
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "SELECT ChunkId FROM RecordChunks ORDER BY random() LIMIT 201";
                    using var reader = command.ExecuteReader();
                    while (reader.Read()) chunks.Add(reader.GetInt32(0));
                }
                await using var reader2 = Store.OpenPublishedReader(copy);
                var first = Stopwatch.StartNew();
                await reader2.ReadChunkAsync(chunks[0]);
                var firstMs = first.Elapsed.TotalMilliseconds;
                var times = new List<double>();
                foreach (var chunk in chunks.Skip(1))
                {
                    var watch = Stopwatch.StartNew();
                    var read = await reader2.ReadChunkAsync(chunk);
                    times.Add(watch.Elapsed.TotalMilliseconds);
                    Assert.NotNull(read);
                }
                times.Sort();
                var headers = Stopwatch.StartNew();
                var rows = await reader2.ReadHeadersAsync();
                var headersMs = headers.Elapsed.TotalMilliseconds;
                using var process = Process.GetCurrentProcess();
                Say($"V7MEASURE run={run} load(start/after-upgrade/end)={loadBefore}/{loadAfterUpgrade}/{Load()} " +
                    $"upgrade-backfill-s={backfill} upgrade-txn-s={upgrade} write-total-s={write.Elapsed.TotalSeconds:F1} " +
                    $"first-chunk-ms={firstMs:F0} chunk-p50-ms={times[times.Count / 2]:F1} chunk-p95-ms={times[(int)(times.Count * 0.95)]:F1} chunk-max-ms={times[^1]:F1} " +
                    $"headers-rows={rows.Count} headers-read-ms={headersMs:F0} test-host-peak-rss-mb={process.PeakWorkingSet64 / 1_048_576}");
            }
            finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
