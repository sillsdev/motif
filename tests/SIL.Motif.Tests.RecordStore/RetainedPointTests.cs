using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed partial class DeletionSafetyTests
{
    [Fact]
    public async Task RepeatedPlanningAndWritesReuseOneVerifiedPointAndAgreeWithAFullScan()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var revision = (await store.ReferencesAsync()).Revision;
        Assert.Equal(1, store.PointAcquisitions);
        var records = await store.ReadAsync();
        var entry = records.Single(r => r.Id == seed.FirstEntryId);
        var homograph = LexEntryTags.kflidHomographNumber;
        var old = entry.Values[homograph.ToString()];
        var bundle = records.First(r => r.ClassId == WfiMorphBundleTags.kClassId &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].ValueKind == JsonValueKind.String &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].GetGuid() == seed.FirstSenseId);
        var first = JsonSerializer.SerializeToElement(seed.FirstSenseId);
        var second = JsonSerializer.SerializeToElement(seed.SecondSenseId);
        var none = JsonSerializer.SerializeToElement<Guid?>(null);
        for (var n = 0; n < 3; n++)
            await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(entry.Id, revision, null));
        var bumped = JsonSerializer.SerializeToElement(old.GetInt32() + 1);
        revision = await store.SetFieldAsync(entry.Id, homograph, old, bumped, revision, Guid.NewGuid());
        revision = await store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense, first, second, revision, Guid.NewGuid());
        revision = await store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense, second, none, revision, Guid.NewGuid());
        Assert.Equal(8, (await store.PlanningIndexAsync()).DeletionImpact(seed.FirstEntryId).NativeLinkCount);
        revision = await store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense, none, second, revision, Guid.NewGuid());
        revision = await store.SetFieldAsync(entry.Id, homograph, bumped, old, revision, Guid.NewGuid());
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(entry.Id, revision, null));
        Assert.Equal(1, store.PointAcquisitions);
        // The retained, incrementally updated point must equal a point built by a fresh full verification.
        var retained = await store.PlanningIndexAsync();
        var all = records.Where(r => r.Kind == "lcm").Select(r => r.Id).ToArray();
        await using var fresh = new Store(Database);
        var verified = await fresh.ReferencesAsync();
        Assert.Equal(1, fresh.PointAcquisitions);
        Assert.Equal(revision, verified.Revision);
        Assert.Equal(verified.DeletionImpact(entry.Id).PlanToken, retained.DeletionImpact(entry.Id).PlanToken);
        Assert.Equal(verified.Incoming(all).Native.ToArray(), retained.Incoming(all).Native.ToArray());
        Assert.Equal(8, verified.DeletionImpact(entry.Id).NativeLinkCount);
        // Full verification on the writing handle, a rebuilt index and a full replay all still agree.
        Assert.Equal(revision, (await store.ReferencesAsync()).Revision);
        Assert.Equal(1, store.PointAcquisitions);
        Assert.Equal(verified.DeletionImpact(entry.Id).PlanToken, (await store.ReferencesAsync()).DeletionImpact(entry.Id).PlanToken);
        await store.RebuildReferenceIndexAsync();
        await store.RegenerateAsync();
        Assert.Equal(verified.DeletionImpact(entry.Id).PlanToken, (await store.ReferencesAsync()).DeletionImpact(entry.Id).PlanToken);
    }

    [Fact]
    public async Task AnotherHandlesWriteNeverLeavesARetainedPointUsable()
    {
        var seed = await Import();
        await using var stale = new Store(Database);
        await using var writer = new Store(Database);
        var revision = (await stale.ReferencesAsync()).Revision;
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => stale.PrepareDeletionAsync(seed.FirstEntryId, revision, null));
        var old = (await writer.ReadAsync()).Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
        var moved = await writer.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber, old,
            JsonSerializer.SerializeToElement(old.GetInt32() + 41), revision, Guid.NewGuid());
        Assert.NotEqual(revision, moved);
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => stale.PrepareDeletionAsync(seed.FirstEntryId, revision, null));
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => stale.SetFieldAsync(seed.FirstEntryId,
            LexEntryTags.kflidHomographNumber, old, JsonSerializer.SerializeToElement(old.GetInt32() + 1), revision, Guid.NewGuid()));
    }

    [Fact]
    public async Task AWriteRefusesAHistoryCommitThatThisHandleDidNotWrite()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var revision = (await store.ReferencesAsync()).Revision;
        var old = (await store.ReadAsync()).Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM pragma_table_info('Commits')";
            var columns = new List<string>();
            using (var reader = command.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(0));
            var copied = string.Join(",", columns.Select(c => c switch
            {
                "Id" => "randomblob(16)",
                "Hash" => "'foreign-' || hex(randomblob(8))",
                _ => "\"" + c + "\""
            }));
            // The foreign commit appears while the guarded write is running, after the retained point was accepted.
            command.CommandText = "CREATE TRIGGER foreign_commit AFTER INSERT ON ChangeEntities BEGIN INSERT INTO Commits SELECT " +
                copied + " FROM Commits LIMIT 1; END";
            command.ExecuteNonQuery();
        }
        var error = await Assert.ThrowsAsync<ForeignCommitException>(() => store.SetFieldAsync(seed.FirstEntryId,
            LexEntryTags.kflidHomographNumber, old, JsonSerializer.SerializeToElement(old.GetInt32() + 1), revision, Guid.NewGuid()));
        Assert.Contains("did not write", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadAsync());
    }
}
