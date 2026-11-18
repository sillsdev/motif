using System.Text.Json;
using SIL.Harmony.Changes;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed class BudgetTests(ITestOutputHelper output) : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-budget-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SplitCreateOfAnOversizeRecordEqualsTheUnsplitApplicationOfItsChanges()
    {
        Directory.CreateDirectory(root);
        var types = new Dictionary<int, int> { [1000] = 27, [1001] = 14, [1002] = 15 };
        var big = new GenericRecord { Id = Guid.NewGuid() };
        big.Values["1000"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 2200).Select(_ => Guid.NewGuid()).ToArray());
        big.Values["1001"] = JsonSerializer.SerializeToElement(Enumerable.Range(0, 300).ToDictionary(n => "w" + n, n => "gloss " + n));
        big.Values["1002"] = JsonSerializer.SerializeToElement("scalar");
        var after = new GenericRecord { Id = Guid.NewGuid() };
        after.Values["1002"] = JsonSerializer.SerializeToElement("next record");
        var imports = new[] { new RecordImport(big, types), new RecordImport(after, types) };
        var prepared = imports.Select(i => new RecordImport(RecordInitialization.Prepare(i.Record, RecordInitialization.Layout([i])), i.FieldTypes)).ToArray();
        var applied = new Dictionary<Guid, GenericRecord>();
        var changeCount = 0;
        foreach (var change in Store.Changes(prepared))
        {
            changeCount++;
            if (change is CreateRecord create) applied[create.EntityId] = await create.NewEntity(null!, null!);
            else await ((EditChange<GenericRecord>)change).ApplyChange(applied[change.EntityId], null!);
        }
        Assert.Equal(2 + 2200 + 300 + 1 + 1, changeCount);
        await using var store = new Store(Path.Combine(root, "records.sqlite"));
        await store.ImportAsync(imports, Guid.NewGuid());
        var commits = await store.CommitsAsync();
        Assert.Equal(changeCount, commits.Sum(c => c.ChangeCount));
        Assert.All(commits, c => Assert.InRange(c.ChangeCount, 1, Store.MaximumChangesPerCommit));
        Assert.True(commits.Count > 5);
        var stored = (await store.ReadAsync()).ToDictionary(r => r.Id);
        Assert.Equal(applied.Count, stored.Count);
        foreach (var (id, expected) in applied)
        {
            var actual = stored[id];
            Assert.Equal(expected.Values.Keys.Order(), actual.Values.Keys.Order());
            foreach (var (key, value) in expected.Values)
                Assert.True(RecordContent.Canonical(value).AsSpan().SequenceEqual(RecordContent.Canonical(actual.Values[key])), key);
            Assert.Equal(expected.IdentityFields, actual.IdentityFields);
            Assert.Equal(expected.Layout.GetRawText(), actual.Layout.GetRawText());
            Assert.Equal(Store.SnapshotItem(expected), Store.SnapshotItem(actual));
        }
    }

    [Fact]
    public async Task OneScalarEditGrowsTheStoreByAtMostTwoKilobytesOfPayload()
    {
        Directory.CreateDirectory(root);
        var project = Path.Combine(root, "Temp");
        var database = Path.Combine(root, "edit.sqlite");
        NativeExpectations native;
        using (var cache = NewLangProjFixture.CreateCache(project))
        {
            SeededProject.SeedText(cache, SeededProject.Seed(cache));
            new FwDataProjectLoader().Save(cache);
            native = new NativeExpectations(cache);
            await LcmRecordBridge.ImportAsync(cache, NewLangProjFixture.FwDataPath(project), database);
        }
        await using var store = new Store(database);
        var entry = (await store.ReadAsync()).Single(r => r.Id == native.DeletionTarget);
        var key = LexEntryTags.kflidHomographNumber.ToString();
        var old = entry.Values[key];
        var next = JsonSerializer.SerializeToElement(old.GetInt32() + 1);
        var revision = (await store.ReferencesAsync()).Revision;
        var before = StorageMeasurement.Read(database);
        var changed = await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, old, next, revision, Guid.NewGuid());
        var edited = StorageMeasurement.Read(database);
        await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, next, old, changed, Guid.NewGuid());
        var restored = StorageMeasurement.Read(database);
        var payload = edited.Tables.Sum(t => t.Payload) - before.Tables.Sum(t => t.Payload);
        output.WriteLine($"One scalar edit: payload +{payload} bytes; allocated +{edited.Tables.Sum(t => t.Bytes) - before.Tables.Sum(t => t.Bytes)} bytes; " +
            $"file total {before.Total} -> {edited.Total} -> {restored.Total}");
        Assert.InRange(payload, 1, 2048);
        Assert.InRange(restored.Tables.Sum(t => t.Payload) - edited.Tables.Sum(t => t.Payload), 1, 2048);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
