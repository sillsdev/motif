using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

[Collection(LcmCacheTestCollection.Name)]
public sealed partial class DeletionSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-deletion-safety-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "records.sqlite");

    [Fact]
    public async Task WarningCountsDistinctAnalysesAndBundlesWithoutCountingInternalOwnershipAsUse()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var native = await store.ReferencesAsync();
        var observations = new ReferenceEvidence(native.Revision, "anchors", "heads", "parser", [
            new(ReferenceWitnessKind.Anchor, "word-1", "anchors", seed.FirstSenseId),
            new(ReferenceWitnessKind.ConflictingHead, "head-1", "heads", seed.FirstLexemeFormId),
            new(ReferenceWitnessKind.ParserReading, "reading-1", "parser", seed.FirstEntryId)]);
        var index = await store.ReferencesAsync(observations);
        var impact = index.DeletionImpact(seed.FirstEntryId);
        Assert.Equal(2, impact.NativeAnalysisCount);
        Assert.Equal(3, impact.NativeBundleCount);
        Assert.Equal(9, impact.NativeLinkCount);
        Assert.Equal(3, impact.Witnesses.Length);
        Assert.Equal(2, impact.Analyses.Length);
        Assert.Equal(3, impact.Bundles.Length);
        Assert.All(impact.Links, link => Assert.False(link.Owning));
        Assert.Contains(seed.FirstSenseId, impact.Targets);
        Assert.Contains(seed.FirstLexemeFormId, impact.Targets);
        Assert.True(impact.NativeComplete);
        var formImpact = index.DeletionImpact(seed.FirstLexemeFormId);
        Assert.True(formImpact.NativeComplete);
        Assert.Equal(2, formImpact.NativeAnalysisCount);
        Assert.Equal(3, formImpact.NativeBundleCount);
        Assert.Equal(3, formImpact.NativeLinkCount);
        Assert.False(impact.Complete);
        Assert.Null(impact.AnalysisCount);
        Assert.Null(impact.LinkCount);
        Assert.False(impact.DeleteAllowed);
        Assert.Throws<IncompleteDeletionClosureException>(() => impact.RequireDeletionAllowed(impact.PlanToken));
        Assert.Throws<IncompleteDeletionClosureException>(() => impact.RequireDeletionAllowed(null));
        Assert.Throws<DeletionPlanMismatchException>(() => impact.RequireDeletionAllowed(formImpact.PlanToken));
        Assert.NotEqual(impact.PlanToken, formImpact.PlanToken);
        Assert.Equal(impact.PlanToken, index.DeletionImpact(seed.FirstEntryId).PlanToken);
        Assert.False(index.DeletionImpact(Guid.NewGuid()).NativeComplete);
        Assert.Empty(typeof(DeletionImpact).GetConstructors());
    }

    [Fact]
    public async Task GuardedFieldWritesPublishFreshReferencesAndNeverAuthorizeIncompleteDeletion()
    {
        var seed = await Import();
        string revision;
        await using (var store = new Store(Database))
        {
            var before = await store.ReadAsync();
            var pinned = await store.ReferencesAsync();
            revision = pinned.Revision;
            var entry = before.Single(r => r.Id == seed.FirstEntryId);
            var key = LexEntryTags.kflidHomographNumber.ToString();
            var old = entry.Values[key];
            var next = JsonSerializer.SerializeToElement(old.GetInt32() + 1);
            var updated = await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, old, next, revision, Guid.NewGuid());
            Assert.NotEqual(revision, updated);
            var current = await store.ReferencesAsync();
            Assert.Equal(updated, current.Revision);
            Assert.Equal(9, pinned.DeletionImpact(entry.Id).NativeLinkCount);
            Assert.Equal(9, current.DeletionImpact(entry.Id).NativeLinkCount);
            Assert.Equal(next.GetInt32(), (await store.ReadAsync()).Single(r => r.Id == entry.Id).Values[key].GetInt32());
            Assert.Equal(pinned.Incoming(before.Where(r => r.Kind == "lcm").Select(r => r.Id)).Native.ToArray(),
                current.Incoming(before.Where(r => r.Kind == "lcm").Select(r => r.Id)).Native.ToArray());
            await Assert.ThrowsAsync<StalePriorValueException>(() => store.SetFieldAsync(entry.Id,
                LexEntryTags.kflidHomographNumber, old, next, updated, Guid.NewGuid()));
            var bundle = (await store.ReadAsync()).First(r => r.ClassId == WfiMorphBundleTags.kClassId &&
                r.Values[WfiMorphBundleTags.kflidSense.ToString()].ValueKind == JsonValueKind.String &&
                r.Values[WfiMorphBundleTags.kflidSense.ToString()].GetGuid() == seed.FirstSenseId);
            // Only the revision is stale: the bundle was not touched and its prior value still matches.
            await Assert.ThrowsAsync<StaleRevisionException>(() => store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense,
                JsonSerializer.SerializeToElement(seed.FirstSenseId), JsonSerializer.SerializeToElement(seed.SecondSenseId), revision, Guid.NewGuid()));
            // Only the prior value differs: the revision is current.
            await Assert.ThrowsAsync<StalePriorValueException>(() => store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense,
                JsonSerializer.SerializeToElement(seed.SecondSenseId), JsonSerializer.SerializeToElement(seed.FirstSenseId), updated, Guid.NewGuid()));
            var oldPlan = pinned.DeletionImpact(entry.Id).PlanToken;
            await Assert.ThrowsAsync<StaleRevisionException>(() => store.PrepareDeletionAsync(entry.Id, revision, oldPlan));
            await Assert.ThrowsAsync<DeletionPlanMismatchException>(() => store.PrepareDeletionAsync(entry.Id, updated, oldPlan));
            await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(entry.Id, updated, current.DeletionImpact(entry.Id).PlanToken));
            revision = await store.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense,
                JsonSerializer.SerializeToElement(seed.FirstSenseId), JsonSerializer.SerializeToElement(seed.SecondSenseId), updated, Guid.NewGuid());
            Assert.Equal(8, (await store.ReferencesAsync()).DeletionImpact(entry.Id).NativeLinkCount);
            await store.RebuildReferenceIndexAsync();
            Assert.Equal(8, (await store.ReferencesAsync()).DeletionImpact(entry.Id).NativeLinkCount);
            await store.RegenerateAsync();
            Assert.Equal(8, (await store.ReferencesAsync()).DeletionImpact(entry.Id).NativeLinkCount);
        }
        await using var reopened = new Store(Database);
        Assert.Equal(revision, (await reopened.ReferencesAsync()).Revision);
        Assert.Equal(8, (await reopened.ReferencesAsync()).DeletionImpact(seed.FirstEntryId).NativeLinkCount);
    }

    [Fact]
    public async Task FieldValidationAndIncompleteDeleteRefusalsLeaveHistoryAndLinksUntouched()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var index = await store.ReferencesAsync();
        var records = await store.ReadAsync();
        var entry = records.Single(r => r.Id == seed.FirstEntryId);
        var count = (await store.CommitsAsync()).Count;
        async Task Refuse(Guid id, int field, JsonElement expected, JsonElement value) =>
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => store.SetFieldAsync(id, field, expected, value, index.Revision, Guid.NewGuid()));
        var old = entry.Values[LexEntryTags.kflidHomographNumber.ToString()];
        await Refuse(entry.Id, 101, entry.Values["101"], JsonSerializer.SerializeToElement(Guid.NewGuid()));
        await Refuse(entry.Id, LexEntryTags.kflidSenses, entry.Values[LexEntryTags.kflidSenses.ToString()], JsonSerializer.SerializeToElement(Array.Empty<Guid>()));
        await Refuse(entry.Id, LexEntryTags.kflidHomographNumber, old, JsonSerializer.SerializeToElement("invalid integer"));
        await Assert.ThrowsAsync<StalePriorValueException>(() => store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber,
            JsonSerializer.SerializeToElement(-99), old, index.Revision, Guid.NewGuid()));
        await Refuse(LcmRecordBridge.ManifestId, 101, old, old);
        var bundle = records.First(r => r.ClassId == WfiMorphBundleTags.kClassId);
        var sense = bundle.Values[WfiMorphBundleTags.kflidSense.ToString()];
        await Refuse(bundle.Id, WfiMorphBundleTags.kflidSense, sense, JsonSerializer.SerializeToElement(Guid.NewGuid()));
        await Refuse(bundle.Id, WfiMorphBundleTags.kflidSense, sense, JsonSerializer.SerializeToElement(entry.Id));
        Assert.Equal(index.Revision, await store.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber, old, old, index.Revision, Guid.NewGuid()));
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(entry.Id, index.Revision, null));
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => store.PrepareDeletionAsync(entry.Id, index.Revision, index.DeletionImpact(entry.Id).PlanToken));
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM IncomingReferences";
        command.ExecuteNonQuery();
        var acquisitions = store.PointAcquisitions;
        Assert.Equal(9, (await store.ReferencesAsync()).DeletionImpact(entry.Id).NativeLinkCount);
        Assert.Equal(acquisitions, store.PointAcquisitions);
        await using var damaged = new Store(Database);
        var partial = (await damaged.ReferencesAsync()).DeletionImpact(entry.Id);
        Assert.False(partial.NativeComplete);
        Assert.Null(partial.NativeAnalysisCount);
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() => damaged.PrepareDeletionAsync(entry.Id, index.Revision, partial.PlanToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => damaged.SetFieldAsync(entry.Id, LexEntryTags.kflidHomographNumber,
            old, JsonSerializer.SerializeToElement(old.GetInt32() + 1), index.Revision, Guid.NewGuid()));
        await store.RebuildReferenceIndexAsync();
        Assert.Equal(9, (await store.ReferencesAsync()).DeletionImpact(entry.Id).NativeLinkCount);
        Assert.Equal(count, (await store.CommitsAsync()).Count);
        Assert.Equal(records.OrderBy(r => r.Id).Select(LcmGraphReader.Canonical), (await store.ReadAsync()).OrderBy(r => r.Id).Select(LcmGraphReader.Canonical));
    }

    [Fact]
    public async Task FailedPublicationRollsBackHistorySnapshotsAndIndexTogether()
    {
        var seed = await Import();
        IReadOnlyList<GenericRecord> before;
        int count;
        string revision;
        await using (var store = new Store(Database))
        {
            before = await store.ReadAsync();
            count = (await store.CommitsAsync()).Count;
            revision = (await store.ReferencesAsync()).Revision;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TRIGGER RejectPublication BEFORE UPDATE ON RecordStoreState BEGIN SELECT RAISE(ABORT, 'reject-publication'); END";
            command.ExecuteNonQuery();
            var prior = before.Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
            var failure = await Record.ExceptionAsync(() => store.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber,
                prior, JsonSerializer.SerializeToElement(prior.GetInt32() + 1), revision, Guid.NewGuid()));
            Assert.NotNull(failure);
            Assert.Contains("reject-publication", failure.ToString());
            await Assert.ThrowsAsync<InvalidOperationException>(store.ReadAsync);
        }
        await using var reopened = new Store(Database);
        Assert.Equal(count, (await reopened.CommitsAsync()).Count);
        Assert.Equal(revision, (await reopened.ReferencesAsync()).Revision);
        Assert.Equal(9, (await reopened.ReferencesAsync()).DeletionImpact(seed.FirstEntryId).NativeLinkCount);
        Assert.Equal(before.OrderBy(r => r.Id).Select(LcmGraphReader.Canonical), (await reopened.ReadAsync()).OrderBy(r => r.Id).Select(LcmGraphReader.Canonical));
    }

    [Fact]
    public async Task EveryStoreWriterRefusesWhileAnotherWriterHoldsTheLease()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var revision = (await store.ReferencesAsync()).Revision;
        var prior = (await store.ReadAsync()).Single(r => r.Id == seed.FirstEntryId).Values[LexEntryTags.kflidHomographNumber.ToString()];
        using (File.Open(Database + ".write.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            await Assert.ThrowsAsync<IOException>(() => store.SetFieldAsync(seed.FirstEntryId, LexEntryTags.kflidHomographNumber,
                prior, JsonSerializer.SerializeToElement(prior.GetInt32() + 1), revision, Guid.NewGuid()));
            await Assert.ThrowsAsync<IOException>(store.RegenerateAsync);
            await Assert.ThrowsAsync<IOException>(() => store.RebuildReferenceIndexAsync());
            await Assert.ThrowsAsync<IOException>(() => store.ImportAsync([], Guid.NewGuid()));
        }
        Assert.Equal(revision, (await store.ReferencesAsync()).Revision);
        Assert.Equal(9, (await store.ReferencesAsync()).DeletionImpact(seed.FirstEntryId).NativeLinkCount);
    }

    [Fact]
    public async Task ACompleteNativeGraphWithoutSemanticMetadataCannotSupplyAnalysisTotals()
    {
        Directory.CreateDirectory(root);
        var entry = new GenericRecord { Id = Guid.NewGuid(), ClassId = LexEntryTags.kClassId };
        var coverage = NativeImportCoverage.Capture([new(entry.Id, entry.ClassId, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(entry.ClassId, ImmutableDictionary<int, int>.Empty), "runtime-fixture");
        await using var store = new Store(Database);
        await store.ImportAsync([new(entry, new Dictionary<int, int>())], Guid.NewGuid(), coverage);
        var index = await store.ReferencesAsync();
        Assert.True(index.Incoming([entry.Id]).NativeComplete);
        var impact = index.DeletionImpact(entry.Id);
        Assert.False(impact.NativeComplete);
        Assert.Null(impact.NativeAnalysisCount);
        Assert.Null(impact.NativeBundleCount);
        Assert.Null(impact.NativeLinkCount);
        Assert.Throws<IncompleteDeletionClosureException>(() => impact.RequireDeletionAllowed(impact.PlanToken));
    }

    private async Task<SeededProject> Import()
    {
        using var cache = NewLangProjFixture.CreateCache(root);
        var seed = SeededProject.Seed(cache);
        var text = SeededProject.SeedText(cache, seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
            var existing = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>().GetObject(text.ApprovedAnalysisId);
            AddBundle(existing);
            var second = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            ((IWfiWordform)existing.Owner).AnalysesOC.Add(second);
            AddBundle(second);
            void AddBundle(IWfiAnalysis analysis)
            {
                var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                analysis.MorphBundlesOS.Add(bundle);
                bundle.MorphRA = entry.LexemeFormOA;
                bundle.SenseRA = entry.SensesOS[0];
                bundle.MsaRA = entry.MorphoSyntaxAnalysesOC.First();
            }
        });
        new FwDataProjectLoader().Save(cache);
        await LcmRecordBridge.ImportAsync(cache, NewLangProjFixture.FwDataPath(root), Database);
        return seed;
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
