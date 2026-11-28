using System.Text.Json;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>Pins the work the record store's hot and diagnostic paths do, so it does not grow with the project again.</summary>
public sealed class RecordStoreHotPathTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-hot-path-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private static GenericRecord Record(int i, Guid? id = null)
    {
        var record = new GenericRecord { Id = id ?? Guid.NewGuid(), Kind = "test", ClassId = i % 7 };
        record.Values["n"] = JsonSerializer.SerializeToElement(i);
        return record;
    }

    private async Task<Store> Build(int count)
    {
        Directory.CreateDirectory(root);
        var store = new Store(Path.Combine(root, Guid.NewGuid().ToString("N") + ".sqlite"));
        await store.ImportAsync(Enumerable.Range(0, count).Select(i => new RecordImport(Record(i), new Dictionary<int, int>())).ToArray(), Guid.NewGuid());
        return store;
    }

    [Fact]
    public async Task WritingToAnEagerPointCostsTheWritesNotTheProject()
    {
        var records = Enumerable.Range(0, 20_000).Select(i => Record(i)).ToDictionary(r => r.Id);
        var source = RecordSource.Eager(records);
        var state = new StoreState();
        var target = records.Values.First();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var n = 0; n < 200; n++)
        {
            source = source.With(Record(n, target.Id), state);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Copying 20,000 entries 200 times would allocate hundreds of megabytes.
        Assert.True(allocated < 2_000_000, $"200 writes allocated {allocated} bytes");
        var latest = await source.GetAsync(target.Id, default);
        Assert.Equal(199, latest!.Values["n"].GetInt32());
        Assert.Same(target, records[target.Id]);
        var other = records.Values.Last();
        Assert.Same(other, await source.GetAsync(other.Id, default));
        Assert.True(source.IsLoaded);
        Assert.Equal(records.Count, source.Loaded.Count);
        Assert.Equal(199, source.Loaded[target.Id].Values["n"].GetInt32());
        Assert.Equal(records.Count, (await source.AllAsync(default)).Count);
    }

    [Fact]
    public void TheLayoutCacheStaysBounded()
    {
        for (var i = 0; i < 5_000; i++)
        {
            var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test", Layout = JsonSerializer.SerializeToElement(new { shape = i }) };
            using var output = new BinaryOutput();
            RecordContent.WriteRecordItem(output, record);
        }
        Assert.True(RecordContent.LayoutCacheCount <= 2_048, $"cache holds {RecordContent.LayoutCacheCount}");
    }

    [Fact]
    public async Task VerificationLoadsTheContentTableOnce()
    {
        await using var store = await Build(50);
        await store.VerifyAsync();
        Assert.Equal(1, store.VerifyContentReads);
    }

    [Fact]
    public async Task VerificationStillDetectsACorruptedRow()
    {
        await using var store = await Build(50);
        var path = Directory.GetFiles(root, "*.sqlite").Single();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE RecordContent SET Data = zeroblob(length(Data)) WHERE Id = (SELECT max(Id) FROM RecordContent)";
            command.ExecuteNonQuery();
        }
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
        Assert.Contains("RecordContent", failure.Message);
        Assert.Equal(1, store.VerifyContentReads);
    }

    [Fact]
    public void WritesKeepTheOrderOfACopiedDictionary()
    {
        var records = Enumerable.Range(0, 5).Select(i => Record(i)).ToDictionary(r => r.Id);
        var ids = records.Keys.ToList();
        var state = new StoreState();
        var source = RecordSource.Eager(records);
        var addedA = Record(100);
        var addedB = Record(101);
        // Update in place, then add two ids, then update an added id: the old copy-and-overwrite order.
        source = source.With(Record(50, ids[3]), state).With(addedB, state).With(addedA, state).With(Record(51, addedB.Id), state).With(Record(52, ids[0]), state);
        var expected = ids.Concat([addedB.Id, addedA.Id]).ToList();
        Assert.Equal(expected, source.Loaded.Keys.ToList());
        Assert.Equal(51, source.Loaded[addedB.Id].Values["n"].GetInt32());
        Assert.Equal(50, source.Loaded[ids[3]].Values["n"].GetInt32());
    }

    [Fact]
    public async Task RescanningDecodesEverySnapshotOnce()
    {
        await using var seed = await Build(50);
        var path = Directory.GetFiles(root, "*.sqlite").Single();
        await using var reader = Store.OpenPublishedReader(path);
        var digests = await reader.RescanDigestsAsync();
        Assert.Equal(50, reader.SnapshotsDecoded);
        Assert.NotNull(digests.Snapshot);
    }
}
