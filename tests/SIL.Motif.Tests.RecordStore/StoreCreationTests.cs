using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed partial class DeletionSafetyTests
{
    [Fact]
    public async Task ACrashWhileCreatingAStoreLeavesNoFileAtItsNameAndTheNextCreateSucceeds()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "created.sqlite");
        Store.BeforeCreationPublished = building =>
        {
            Assert.True(File.Exists(building));
            Assert.False(File.Exists(path));
            throw new Crash();
        };
        try { Assert.Throws<Crash>(() => new Store(path)); }
        finally { Store.BeforeCreationPublished = null; }
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + ".creating"));
        // A kill leaves the half-built file behind; the next create discards it.
        File.WriteAllBytes(path + ".creating", [1, 2, 3, 4]);
        await using (new Store(path)) { }
        Assert.False(File.Exists(path + ".creating"));
        Assert.Equal(7L, Scalar("PRAGMA user_version", path));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM RecordStoreState", path));
        await using var reopened = new Store(path);
    }

    [Fact]
    public async Task AStaleSidecarBesideAMissingStoreIsDiscardedBeforeTheNewStoreIsBuilt()
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "stale.sqlite");
        foreach (var suffix in new[] { "-wal", "-shm", "-journal" }) File.WriteAllBytes(path + suffix, [9, 9, 9, 9]);
        await using (new Store(path)) { }
        Assert.Equal(1L, Scalar("SELECT count(*) FROM RecordStoreState", path));
        Assert.DoesNotContain((byte)9, File.Exists(path + "-wal") ? File.ReadAllBytes(path + "-wal") : []);
        Assert.False(File.Exists(path + "-journal"));
        await using var reopened = new Store(path);
    }

    [Fact]
    public async Task AFailedIndexRebuildClearsTheTrackerAndRefusesFurtherUseOfTheHandle()
    {
        var seed = await Import();
        var before = Scalar("SELECT IndexDigest FROM RecordStoreState");
        await using var store = new Store(Database);
        var revision = (await store.ReferencesAsync()).Revision;
        var old = (await store.ReadAsync()).Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
        store.WriteStageReached = stage => { if (stage == WriteStage.StateWritten) throw new Crash(); };
        await Assert.ThrowsAsync<Crash>(() => store.RebuildReferenceIndexAsync());
        store.WriteStageReached = null;
        Assert.Equal(before, Scalar("SELECT IndexDigest FROM RecordStoreState"));
        // The rolled-back state row must not be flushed into a later write's transaction.
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber,
            old, JsonSerializer.SerializeToElement(old.GetInt32() + 1), revision, Guid.NewGuid()));
        Assert.Equal(before, Scalar("SELECT IndexDigest FROM RecordStoreState"));
        await using var reopened = new Store(Database);
        await reopened.VerifyAsync();
    }
}
