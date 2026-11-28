using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed partial class DeletionSafetyTests
{
    private const string TraversalLimitation = "Native traversal does not match the retained graph.";

    private string StoredLimitations() => (string)Scalar("SELECT IndexLimitations FROM RecordStoreState")!;

    [Fact]
    public async Task AListEditKeepsTheNativeClosureCompleteSoLaterFieldWritesAndDeletionPlanningStillWork()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var before = await store.ReferencesAsync();
        Assert.DoesNotContain(TraversalLimitation, StoredLimitations());
        Guid? moved = null;
        foreach (var record in await store.ReadAsync())
        {
            foreach (var (key, value) in record.Values)
                if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= 2 &&
                    RecordInitialization.Type(record, key) is >= 26 and <= 28 &&
                    ListAuthoring.Move(record, int.Parse(key), Guid.NewGuid(), 0, 1) is { } move)
                {
                    await store.CommitListOperationsAsync([move], Guid.NewGuid());
                    moved = record.Id;
                    break;
                }
            if (moved is not null) break;
        }
        Assert.NotNull(moved);
        var after = await store.ReferencesAsync();
        Assert.DoesNotContain(TraversalLimitation, StoredLimitations());
        await using var fresh = new Store(Database);
        Assert.DoesNotContain(TraversalLimitation, StoredLimitations());
        Assert.NotNull(await fresh.ReferencesAsync());

        var entry = (await store.ReadAsync()).Single(r => r.Id == seed.FirstEntryId);
        var old = entry.Values[LexEntryTags.kflidHomographNumber.ToString()];
        var next = JsonSerializer.SerializeToElement(old.GetInt32() + 1);
        var revision = await store.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber, old, next, after.Revision, Guid.NewGuid());
        // The seeded entry stays undeletable for its own reasons, but never because of the list edit.
        var refusal = await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(seed.FirstEntryId, revision, null));
        Assert.DoesNotContain(TraversalLimitation, refusal.Message);
        await store.RebuildReferenceIndexAsync();
        Assert.DoesNotContain(TraversalLimitation, StoredLimitations());
    }
}
