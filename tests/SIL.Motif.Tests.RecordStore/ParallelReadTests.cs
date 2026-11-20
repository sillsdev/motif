using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>Parallel decoding and digesting return and refuse exactly what the sequential read did.</summary>
public sealed class ParallelReadTests : IDisposable
{
    private const int ListField = 1000;
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-parallel-read-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    /// <summary>Plain, nested, text-shaped and list records; some lists are edited.</summary>
    private async Task<string> Build(string name, int count = 300)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name + ".sqlite");
        var random = new Random(7);
        var imports = new List<RecordImport>();
        for (var i = 0; i < count; i++)
        {
            var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test", ClassId = i % 7 };
            record.Values["name"] = JsonSerializer.SerializeToElement("name " + i);
            record.Values["n"] = JsonSerializer.SerializeToElement(i * 31);
            record.Values["ref"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
            record.Values["nested"] = JsonSerializer.SerializeToElement(new { a = new { b = new[] { 1, 2, i } }, t = "x" + i % 11 });
            if (i % 9 == 0) record.Values["rich"] = JsonSerializer.SerializeToElement(new { text = "t" + i, utf16le = (string?)null });
            if (i % 4 == 0)
                record.Values[ListField.ToString()] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 3 + random.Next(5)).Select(_ => Guid.NewGuid()).ToArray());
            imports.Add(new(record, new Dictionary<int, int> { [ListField] = 27 }));
        }
        await using var store = new Store(path);
        await store.ImportAsync(imports, Guid.NewGuid());
        var lists = (await store.ReadAsync()).Where(r => r.Values.ContainsKey(ListField.ToString())).Take(12).ToArray();
        foreach (var record in lists)
            await store.CommitListOperationsAsync([ListAuthoring.Insert(record, ListField, Guid.NewGuid(), Guid.NewGuid(), 1)], Guid.NewGuid());
        return path;
    }

    private static byte[][] Items(IEnumerable<GenericRecord> records) => records.Select(Store.SnapshotItem).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task EveryWorkerCountReadsTheSameRecordsInTheSameOrderAndVerifiesTheSameDigest(int workers)
    {
        var path = await Build("equal");
        await using var sequential = new Store(path) { Workers = 1, DecodeChunkSize = 1_000_000 };
        var expected = await sequential.ReadAsync();
        await using var store = new Store(path) { Workers = workers, DecodeChunkSize = 7 };
        // ReadAsync verifies the state row's digest, written by the sequential import path, against the fused one.
        var actual = await store.ReadAsync();
        Assert.Equal(expected.Select(r => r.Id), actual.Select(r => r.Id));
        Assert.Equal(Items(expected), Items(actual));
        Assert.Equal(expected.Select(r => r.Lists.Count), actual.Select(r => r.Lists.Count));
        Assert.Contains(actual, r => r.Lists.Any(l => l.Value.Ops.Count > 0));
        var point = await store.ReferencesAsync();
        Assert.Equal((await sequential.ReferencesAsync()).Revision, point.Revision);
        Assert.Equal(actual.Count * 2, store.SnapshotsDecoded);
    }

    [Fact]
    public void PartialDigestsCombineToTheSequentialDigest()
    {
        var random = new Random(3);
        var items = Enumerable.Range(0, 500).Select(_ => random.GetItems<byte>(Enumerable.Range(0, 255).Select(x => (byte)x).ToArray(), 1 + random.Next(80))).ToArray();
        var whole = new SetDigest();
        foreach (var item in items) whole = whole.Add(item);
        foreach (var parts in new[] { 1, 2, 3, 8, 500 })
        {
            var sum = new SetDigest();
            for (var part = 0; part < parts; part++)
            {
                var partial = new SetDigest();
                for (var i = part; i < items.Length; i += parts) partial = partial.Add(items[i]);
                sum = sum.Combine(partial);
            }
            Assert.Equal(whole, sum);
            Assert.Equal(whole.Final("x"), sum.Final("x"));
        }
        var edited = whole.Remove(items[0]).Add([1, 2, 3]);
        var rebuilt = new SetDigest();
        foreach (var item in items.Skip(1)) rebuilt = rebuilt.Add(item);
        Assert.Equal(edited, rebuilt.Add([1, 2, 3]));
    }

    private static void Tamper(string path, Action<SqliteCommand> change)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=OFF";
        command.ExecuteNonQuery();
        change(command);
    }

    private static async Task<(Type, string)> Refusal(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        return (error.GetType(), error.Message);
    }

    [Theory]
    [InlineData("snapshot-version", "Unknown record codec version.")]
    [InlineData("snapshot-digest", "The snapshot projection is corrupt; regenerate it from history before use.")]
    [InlineData("replay-digest", "History is corrupt; the existing snapshot projection has been preserved.")]
    [InlineData("change-row", "History is corrupt; the existing snapshot projection has been preserved.")]
    [InlineData("two-snapshots", "Truncated record payload.")]
    public async Task ACorruptedTableIsRefusedWithTheErrorTheSequentialReadGave(string kind, string message)
    {
        var results = new List<(Type, string)>();
        foreach (var workers in new[] { 1, 4, 8 })
        {
            var path = await Build($"corrupt-{kind}-{workers}", 80);
            Guid[] ids;
            await using (var reader = new Store(path)) ids = (await reader.ReadAsync()).Select(r => r.Id).ToArray();
            Tamper(path, command =>
            {
                switch (kind)
                {
                    case "snapshot-version":
                        command.CommandText = "UPDATE Snapshots SET Entity = x'04' || substr(Entity, 2) WHERE rowid = (SELECT max(rowid) FROM Snapshots)";
                        break;
                    case "snapshot-digest":
                        command.CommandText = "UPDATE RecordStoreState SET SnapshotDigest='" + new string('0', 64) + "'";
                        break;
                    case "replay-digest":
                        command.CommandText = "UPDATE RecordStoreState SET ReplayDigest='" + new string('0', 64) + "'";
                        break;
                    case "change-row":
                        command.CommandText = "UPDATE ChangeEntities SET Change = x'ff' WHERE rowid = (SELECT max(rowid) FROM ChangeEntities)";
                        break;
                    default:
                        // An early truncation and a later bad version: the sequential read meets the truncation first.
                        command.CommandText = $"""
                            UPDATE Snapshots SET Entity = substr(Entity, 1, 3) WHERE EntityId = x'{ids[2].ToString("N")}';
                            UPDATE Snapshots SET Entity = x'04' || substr(Entity, 2) WHERE EntityId = x'{ids[^3].ToString("N")}'
                            """;
                        break;
                }
                command.ExecuteNonQuery();
            });
            await using var store = new Store(path) { Workers = workers, DecodeChunkSize = 3 };
            results.Add(await Refusal(kind is "replay-digest" or "change-row" ? () => store.RegenerateAsync() : () => store.ReadAsync()));
        }
        // Type and message are those the code gave before parallel decoding.
        Assert.All(results, result => Assert.Equal((typeof(InvalidDataException), message), result));
    }

    [Fact]
    public async Task TheReplayScanAgreesAtEveryWorkerCountAndBatchSize()
    {
        var path = await Build("replay", 400);
        await using var store = new Store(path);
        var state = (await store.ReferencesAsync()).Revision;
        store.Workers = 1;
        var expected = await store.ScanHistoryAsync();
        foreach (var workers in new[] { 1, 2, 4, 8 })
        foreach (var batch in new[] { 1, 5, 64, 100_000 })
        {
            store.Workers = workers;
            var scan = await store.ScanHistoryAsync(batch);
            Assert.Equal(expected.Final(), scan.Final());
            Assert.Equal(expected.Digest, scan.Digest);
            Assert.True(expected.Commits.SetEquals(scan.Commits));
        }
        Assert.NotEmpty(state);
    }

    [Fact]
    public async Task ABadCommitInALateRangeIsReportedAsTheSequentialScanReportsIt()
    {
        var path = await Build("badcommit", 400);
        Tamper(path, command =>
        {
            command.CommandText = "SELECT count(*) FROM Commits";
            var commits = (long)command.ExecuteScalar()!;
            Assert.True(commits >= 6);
            // Two failures of different kinds: the lower rowid's wins on every worker count.
            command.CommandText = $"""
                UPDATE Commits SET Id = x'abcd' WHERE rowid = (SELECT rowid FROM Commits ORDER BY rowid LIMIT 1 OFFSET {commits - 3});
                UPDATE Commits SET Id = 'text' WHERE rowid = (SELECT max(rowid) FROM Commits)
                """;
            command.ExecuteNonQuery();
        });
        await using var store = new Store(path);
        var results = new List<(Type, string)>();
        foreach (var workers in new[] { 1, 2, 4, 8 })
        {
            store.Workers = workers;
            results.Add(await Refusal(() => store.ScanHistoryAsync(batchRows: 1)));
        }
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.Equal(typeof(ArgumentException), results[0].Item1);
    }

    [Fact]
    public async Task CancellingMidDecodeThrowsAndLeavesTheHandleReusable()
    {
        var path = await Build("cancel-decode", 400);
        await using var store = new Store(path) { Workers = 4, DecodeChunkSize = 5 };
        using var cancel = new CancellationTokenSource();
        store.DecodeChunkStarted = chunk => { if (chunk == 6) cancel.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.VerifiedPointAsync(cancel.Token));
        Assert.Equal(0, store.PointAcquisitions);
        store.DecodeChunkStarted = null;
        var index = await store.ReferencesAsync();
        Assert.Equal(1, store.PointAcquisitions);
        Assert.NotEmpty(index.Revision);
    }

    [Fact]
    public async Task CancellingMidScanThrows()
    {
        var path = await Build("cancel-scan", 400);
        foreach (var workers in new[] { 1, 4 })
        {
            await using var store = new Store(path) { Workers = workers };
            using var cancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ScanHistoryAsync(batchRows: 3, cancel.Token,
                (table, batch) => { if (table == "ChangeEntities" && batch == 4) cancel.Cancel(); }));
            Assert.Equal((await store.ScanHistoryAsync()).Final(), (await store.ScanHistoryAsync(1)).Final());
        }
    }

    [Fact]
    public async Task ASecondReferencesCallOnTheSameHandleDoesNotDecodeAgainButAnotherHandlesWriteDoes()
    {
        var path = await Build("reuse", 200);
        await using var store = new Store(path);
        var first = await store.ReferencesAsync();
        var decoded = store.SnapshotsDecoded;
        Assert.True(decoded > 0);
        Assert.Equal(1, store.PointAcquisitions);
        for (var n = 0; n < 3; n++)
        {
            Assert.Equal(first.Revision, (await store.ReferencesAsync()).Revision);
            Assert.Equal(first.Revision, (await store.ReferencesAsync(new ReferenceEvidence(first.Revision, "a", "h", "p", []))).Revision);
        }
        Assert.Equal(decoded, store.SnapshotsDecoded);
        Assert.Equal(1, store.PointAcquisitions);
        // Neither a local write nor another handle's write leaves a reusable point.
        var target = (await store.ReadAsync()).First(r => r.Values.ContainsKey(ListField.ToString()));
        await store.CommitListOperationsAsync([ListAuthoring.Insert(target, ListField, Guid.NewGuid(), Guid.NewGuid(), 0)], Guid.NewGuid());
        var second = await store.ReferencesAsync();
        Assert.NotEqual(first.Revision, second.Revision);
        Assert.Equal(2, store.PointAcquisitions);
        await using (var other = new Store(path))
        {
            var again = (await other.ReadAsync()).First(r => r.Id == target.Id);
            await other.CommitListOperationsAsync([ListAuthoring.Insert(again, ListField, Guid.NewGuid(), Guid.NewGuid(), 0)], Guid.NewGuid());
        }
        var before = store.SnapshotsDecoded;
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => store.ReferencesAsync());
        Assert.Equal(before, store.SnapshotsDecoded);
        await using var fresh = new Store(path);
        Assert.NotEqual(second.Revision, (await fresh.ReferencesAsync()).Revision);
    }

    [Fact]
    public async Task ContentLookupsAndTheCodecAreSafeFromManyThreads()
    {
        var path = await Build("threads", 150);
        // Every stored snapshot, decoded once on one thread and then by many threads sharing one content cache.
        var blobs = new List<byte[]>();
        var rows = new List<ContentRow>();
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Entity FROM Snapshots";
            using (var reader = command.ExecuteReader()) while (reader.Read()) blobs.Add((byte[])reader[0]);
            command.CommandText = "SELECT Id, Data FROM RecordContent";
            using (var reader = command.ExecuteReader()) while (reader.Read()) rows.Add(new ContentRow { Id = (int)reader.GetInt64(0), Data = (byte[])reader[1] });
        }
        var reference = new RecordContent();
        reference.Load(rows.Select(r => new ContentRow { Id = r.Id, Data = r.Data }).ToArray());
        var expected = new RecordCodec(reference);
        var wanted = blobs.Select(blob => Store.SnapshotItem((GenericRecord)expected.Snapshot(blob))).ToArray();
        var shared = new RecordContent();
        shared.Load(rows);
        var codec = new RecordCodec(shared);
        var seen = new ConcurrentDictionary<int, JsonElement>();
        var failures = new ConcurrentQueue<string>();
        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 16 }, thread =>
        {
            var random = new Random(thread);
            for (var round = 0; round < 3; round++)
            for (var n = 0; n < blobs.Count; n++)
            {
                var index = (n * 7 + thread * 13 + round) % blobs.Count;
                var record = (GenericRecord)codec.Snapshot(blobs[index]);
                if (!Store.SnapshotItem(record).AsSpan().SequenceEqual(wanted[index])) failures.Enqueue("record " + index);
                var id = rows[random.Next(rows.Count)].Id;
                var value = shared.Get(id);
                if (!seen.GetOrAdd(id, value).Equals(value)) failures.Enqueue("content " + id + " decoded to two elements");
            }
        });
        Assert.Empty(failures);
        // Every decoded row is counted once however many threads raced to decode it.
        Assert.True(shared.DecodedCount <= rows.Count);
    }

    [Fact]
    public async Task AFailureFollowedByCancellationThrowsCancellationOrTheFailureNeverAnAggregate()
    {
        var path = await Build("fail-cancel", 40);
        Guid[] ids;
        await using (var reader = new Store(path)) ids = (await reader.ReadAsync()).Select(r => r.Id).ToArray();
        Tamper(path, command =>
        {
            command.CommandText = $"UPDATE Snapshots SET Entity = x'04' || substr(Entity, 2) WHERE EntityId = x'{ids[7].ToString("N")}'";
            command.ExecuteNonQuery();
        });
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await using var store = new Store(path) { Workers = 4, DecodeChunkSize = 2 };
            using var cancel = new CancellationTokenSource();
            var failed = new ManualResetEventSlim();
            store.DecodeChunkStarted = chunk =>
            {
                if (chunk == 3) { failed.Set(); Thread.Sleep(200); }
                if (chunk == 0) { failed.Wait(TimeSpan.FromSeconds(3)); Thread.Sleep(100); cancel.Cancel(); }
            };
            var error = await Assert.ThrowsAnyAsync<Exception>(() => store.VerifiedPointAsync(cancel.Token));
            Assert.True(error is OperationCanceledException || error is InvalidDataException, error.GetType() + ": " + error.Message);
        }
    }

    [Theory]
    [InlineData(0, 4, 1024)]
    [InlineData(1, 4, 1024)]
    [InlineData(1, 4, 1)]
    [InlineData(3, 8, 1000)]
    public async Task EmptySingleAndShortStoresReadAndVerifyAtSeveralWorkers(int count, int workers, int chunk)
    {
        var path = await Build($"small-{count}-{workers}-{chunk}", count);
        await using var sequential = new Store(path) { Workers = 1 };
        await using var store = new Store(path) { Workers = workers, DecodeChunkSize = chunk };
        var records = await store.ReadAsync();
        Assert.Equal(count, records.Count);
        Assert.Equal((await sequential.ReadAsync()).Select(r => r.Id), records.Select(r => r.Id));
        Assert.NotEmpty((await store.ReferencesAsync()).Revision);
    }
}
