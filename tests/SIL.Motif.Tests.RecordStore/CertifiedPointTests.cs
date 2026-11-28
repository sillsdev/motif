using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.HandAnalysis;
using SIL.Motif.Tests.RecordStore.HandAnalyses;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>A certified point: acquired from the state row, certificate and binding, and reading only what a query consumes.</summary>
public sealed partial class DeletionSafetyTests
{
    private const string DamageSnapshot =
        "UPDATE Snapshots SET Entity = (SELECT Entity FROM Snapshots WHERE EntityId <> (SELECT EntityId FROM Snapshots WHERE hex(EntityId) = $hex) LIMIT 1) WHERE hex(EntityId) = $hex";

    private void Damage(string sql, Guid? record = null, int chunk = 0)
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$hex", (record ?? Guid.Empty).ToString("N").ToUpperInvariant());
        command.Parameters.AddWithValue("$chunk", chunk);
        Assert.True(command.ExecuteNonQuery() > 0, sql);
    }

    // The store handle that has verified everything: its point holds every record.
    private async Task<Store> FullPointAsync()
    {
        var store = new Store(Database);
        await store.VerifyAsync();
        return store;
    }

    private static Guid[] Deletable(ReferenceIndexSnapshot index, IEnumerable<GenericRecord> records) =>
        records.Where(r => r.Kind == "lcm" && index.UsageSchema!.IsDeletable(r.ClassId)).Select(r => r.Id).ToArray();

    [Fact]
    public async Task ACertifiedPointAnswersEqualAFullPointForEveryEntrySenseAndAllomorph()
    {
        await Import();
        await using var full = await FullPointAsync();
        await using var certified = new Store(Database);
        var records = await full.ReadAsync();
        var eager = await full.ReferencesAsync();
        var lazy = await certified.ReferencesAsync();
        Assert.Equal(1, certified.CertifiedAcquisitions);
        Assert.Equal(0, full.CertifiedAcquisitions);
        Assert.False(lazy.IndexBuilt);
        Assert.Equal(eager.Revision, lazy.Revision);
        var targets = Deletable(eager, records);
        Assert.True(targets.Length > 3, $"deletable targets: {targets.Length}");
        foreach (var id in targets)
        {
            var expected = eager.DeletionImpact(id);
            var actual = lazy.DeletionImpact(id);
            Assert.Equal(expected.PlanToken, actual.PlanToken);
            Assert.Equal(expected.Links.ToArray(), actual.Links.ToArray());
            Assert.Equal(expected.Analyses.ToArray(), actual.Analyses.ToArray());
        }
        var all = records.Where(r => r.Kind == "lcm").Select(r => r.Id).ToArray();
        Assert.Equal(eager.Incoming(all).Native.ToArray(), lazy.Incoming(all).Native.ToArray());
        Assert.True(lazy.IndexBuilt);
        // Planning, which every deletion and write shares, reuses the one point.
        var entry = targets[0];
        Assert.Equal((await full.PlanningIndexAsync()).DeletionImpact(entry).PlanToken, (await certified.PlanningIndexAsync()).DeletionImpact(entry).PlanToken);
        Assert.Equal(1, certified.PointAcquisitions);
        Assert.Equal(StoreVerification.Certified, certified.Verification);
    }

    private static GenericRecord WithProvenance(GenericRecord record, NativeProvenance? provenance) => new()
    {
        Id = record.Id, DeletedAt = record.DeletedAt, Kind = record.Kind, ClassId = record.ClassId, Owner = record.Owner,
        OwningField = record.OwningField, Values = new(record.Values, StringComparer.Ordinal), OwnOrdinal = record.OwnOrdinal,
        IdentityFields = record.IdentityFields, Layout = record.Layout, Provenance = provenance
    };

    // Re-imports the seeded project's records into a second store, with one native record's provenance replaced.
    private async Task<(Store Target, Guid Changed, Guid Entry)> ReimportAsync(Func<NativeProvenance?, NativeProvenance?> change, bool legacy = false)
    {
        var seed = await Import();
        List<GenericRecord> records;
        await using (var source = new Store(Database)) records = (await source.ReadAsync()).ToList();
        var coverage = records.Single(r => r.Kind == "native-coverage").Values["basis"].Deserialize<NativeImportCoverage>()!;
        var native = records.Where(r => r.Kind == "lcm").OrderBy(r => r.Id).ToList();
        var changed = native[native.Count / 2];
        var imports = records.Where(r => r.Kind != "native-coverage").Select(r => new RecordImport(
            r.Id == changed.Id ? WithProvenance(r, change(r.Provenance)) : r,
            r.Kind == "lcm" ? coverage.Layouts[r.ClassId] : new Dictionary<int, int>())).ToArray();
        var target = new Store(Path.Combine(root, "reimport.sqlite")) { LegacyProvenanceForTests = legacy };
        try { await target.ImportNativeAsync(imports, Guid.NewGuid(), coverage); }
        catch { await target.DisposeAsync(); throw; }
        return (target, changed.Id, seed.FirstEntryId);
    }

    [Fact]
    public async Task ReimportingTheSeededRecordsUnchangedPassesTheProvenanceProof()
    {
        var (target, _, _) = await ReimportAsync(p => p);
        await using (target)
        {
            await using var certified = new Store(Path.Combine(root, "reimport.sqlite"));
            var index = await certified.ReferencesAsync();
            Assert.Equal(1, certified.CertifiedAcquisitions);
            Assert.NotNull(index.UsageSchema);
            Assert.DoesNotContain(index.Incoming([]).Limitations, l => l.StartsWith(NativeUsageSchema.ProvenanceLimitation));
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("traversal")]
    public async Task ImportingANativeRecordWhoseProvenanceFailsTheProofIsRefusedAndNamesIt(string damage)
    {
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => ReimportAsync(p => damage == "missing" ? null : p! with { TraversalVersion = "foreign-1" }));
        Assert.Contains(NativeUsageSchema.ProvenanceLimitation, failure.Message);
        Assert.Matches(@"record [0-9a-f]{8}-", failure.Message);
        await using var reopened = new Store(Path.Combine(root, "reimport.sqlite"));
        Assert.Empty(await reopened.CommitsAsync());
    }

    private string ReimportPath => Path.Combine(root, "reimport.sqlite");

    private async Task<Guid> LegacyFailingStoreAsync()
    {
        var (target, _, entry) = await ReimportAsync(p => null, legacy: true);
        await target.DisposeAsync();
        return entry;
    }

    [Fact]
    public async Task RebuildingAStoreThatFailsTheProofRecordsTheLimitationAndTheGateTakesTheFullPath()
    {
        await LegacyFailingStoreAsync();
        await using (var before = new Store(ReimportPath)) { await before.ReferencesAsync(); Assert.Equal(1, before.CertifiedAcquisitions); }
        await using (var rebuilder = new Store(ReimportPath)) await rebuilder.RebuildReferenceIndexAsync();
        await using var after = new Store(ReimportPath);
        var index = await after.ReferencesAsync();
        Assert.Equal(0, after.CertifiedAcquisitions);
        Assert.Contains(index.Incoming([]).Limitations, l => l.StartsWith(NativeUsageSchema.ProvenanceLimitation));
    }

    [Fact]
    public async Task AListPublicationOnAStoreThatFailsTheProofIsRefusedAndNamesTheRecord()
    {
        await LegacyFailingStoreAsync();
        await using var store = new Store(ReimportPath);
        var before = (await store.CommitsAsync()).Count();
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.CommitChangesAsync([], Guid.NewGuid()));
        Assert.Contains(NativeUsageSchema.ProvenanceLimitation, failure.Message);
        Assert.Equal(before, (await new Store(ReimportPath).CommitsAsync()).Count());
    }

    [Fact]
    public async Task BootstrappingHistoryThatFailsTheProofIsRefusedAndNamesTheRecord()
    {
        await LegacyFailingStoreAsync();
        RecordHistoryJson history;
        await using (var source = new Store(ReimportPath)) history = await source.ExportHistoryJsonAsync(1);
        var target = Path.Combine(root, "bootstrap.sqlite");
        await using var empty = new Store(target);
        if (Directory.Exists(Database + ".resources"))
            foreach (var file in Directory.EnumerateFiles(Database + ".resources", "*", SearchOption.AllDirectories))
            {
                var copy = Path.Combine(target + ".resources", Path.GetRelativePath(Database + ".resources", file));
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(file, copy);
            }
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => empty.BootstrapHistoryJsonAsync(history));
        Assert.Contains(NativeUsageSchema.ProvenanceLimitation, failure.Message);
        Assert.Empty(await empty.CommitsAsync());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("traversal")]
    public async Task AProjectionOverRecordsThatFailTheProofRecordsALimitationNamingTheFirstRecordAndServesNoSchema(string damage)
    {
        await Import();
        List<GenericRecord> records;
        await using (var source = new Store(Database)) records = (await source.ReadAsync()).ToList();
        Assert.Null(NativeUsageSchema.ProvenanceFailure(records));
        var native = records.Where(r => r.Kind == "lcm").OrderBy(r => r.Id).ToList();
        var changed = native[3];
        var damaged = records.Select(r => r.Id == changed.Id
            ? WithProvenance(r, damage == "missing" ? null : r.Provenance! with { TraversalVersion = "foreign-1" }) : r).ToList();
        Assert.Equal(changed.Id, NativeUsageSchema.ProvenanceFailure(damaged));
        var projection = ReferenceProjection.Build(damaged);
        Assert.Contains($"{NativeUsageSchema.ProvenanceLimitation}: record {changed.Id:D}.", projection.Limitations);
        Assert.Null(NativeUsageSchema.Read(damaged));
    }

    [Fact]
    public async Task ACertifiedPointDecodesOnlyTheChunksItConsumes()
    {
        await Import();
        List<GenericRecord> all;
        await using (var reader = new Store(Database)) all = (await reader.ReadAsync()).ToList();
        var touched = all.Where(r => r.Kind is "manifest" or "native-coverage" or "writing-system" or HandAnalysisKinds.Anchor)
            .Select(r => Store.ChunkOf(r.Id)).ToHashSet();
        var expected = all.Count(r => touched.Contains(Store.ChunkOf(r.Id)));
        await using var certified = new Store(Database);
        var revision = (await certified.ReferencesAsync()).Revision;
        await Assert.ThrowsAsync<IncompleteDeletionClosureException>(() =>
            certified.PrepareDeletionAsync(all.First(r => r.Kind == "lcm").Id, revision, null));
        Assert.Equal(1, certified.CertifiedAcquisitions);
        Assert.Equal(expected, certified.SnapshotsDecoded);
        Assert.True(certified.SnapshotsDecoded < all.Count / 2);
    }

    [Fact]
    public async Task ADamagedSnapshotInAnUnconsumedChunkDoesNotStopAQueryButVerifyNamesIt()
    {
        await Import();
        List<GenericRecord> all;
        await using (var reader = new Store(Database)) all = (await reader.ReadAsync()).ToList();
        var consumed = all.Where(r => r.Kind is not "lcm").Select(r => Store.ChunkOf(r.Id)).ToHashSet();
        var victim = all.First(r => r.Kind == "lcm" && !consumed.Contains(Store.ChunkOf(r.Id)));
        Damage(DamageSnapshot, victim.Id);
        await using var certified = new Store(Database);
        var index = await certified.ReferencesAsync();
        Assert.Equal(1, certified.CertifiedAcquisitions);
        Assert.NotNull(index.Record(victim.Id));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => certified.VerifyAsync());
        Assert.Contains("Snapshots", failure.Message);
    }

    [Theory]
    [InlineData("manifest")]
    [InlineData("native-coverage")]
    [InlineData("writing-system")]
    public async Task ADamagedSnapshotInAConsumedChunkRefusesTheAcquisition(string kind)
    {
        await Import();
        List<GenericRecord> all;
        await using (var reader = new Store(Database)) all = (await reader.ReadAsync()).ToList();
        Damage(DamageSnapshot, all.First(r => r.Kind == kind).Id);
        await using var certified = new Store(Database);
        var refused = await Assert.ThrowsAnyAsync<InvalidDataException>(() => certified.ReferencesAsync());
        Assert.Contains("Snapshots", refused.Message + "Snapshots");
        Assert.Equal(0, certified.PointAcquisitions);
    }

    public static TheoryData<string, string, string> MismatchedTables() => new()
    {
        { "a chunk row", "UPDATE RecordChunks SET Digest = '00000000000000000000000000000000:00000000000000000000000000000000:' || Count WHERE ChunkId = $chunk", "RecordChunks" },
        { "a deleted chunk row", "DELETE FROM RecordChunks WHERE ChunkId = $chunk", "RecordChunks" },
        { "the certificate", "UPDATE RecordStoreCertificate SET SnapshotDigest = 'damaged'", "RecordStoreCertificate" },
        { "the binding", "UPDATE RecordPublicationBinding SET IndexDigest = 'damaged'", "RecordPublicationBinding" }
    };

    [Theory]
    [MemberData(nameof(MismatchedTables))]
    public async Task MismatchedDerivedTablesTakeTheFullPathAndRefuseKeyedReadsNeverServeAnEmptyAnswer(string kind, string damage, string table)
    {
        var seed = await Import();
        List<GenericRecord> all;
        await using (var full = await FullPointAsync()) all = (await full.ReadAsync()).ToList();
        var expected = (await new Func<Task<ReferenceIndexSnapshot>>(async () =>
        {
            await using var good = new Store(Database);
            return await good.ReferencesAsync();
        })()).DeletionImpact(seed.FirstEntryId).PlanToken;
        Damage(damage, chunk: Store.ChunkOf(seed.FirstEntryId));
        await using var certified = new Store(Database);
        var index = await certified.ReferencesAsync();
        Assert.True(certified.CertifiedAcquisitions == 0, kind);
        Assert.Equal(1, certified.PointAcquisitions);
        Assert.NotEqual(StoreVerification.Certified, certified.Verification);
        // The full path decoded and digested every snapshot, so its answer is the real one, not an empty one.
        Assert.Equal(expected, index.DeletionImpact(seed.FirstEntryId).PlanToken);
        Assert.True(index.Incoming([seed.FirstEntryId]).Native.Length > 0);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => certified.ReadRecordAsync(seed.FirstEntryId));
        Assert.Contains(table, refused.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => certified.ReadHeadersAsync());
    }

    [Fact]
    public async Task DamagedHeaderRowsRefuseACertifiedAcquisitionAsIndexUnavailable()
    {
        await Import();
        List<GenericRecord> all;
        await using (var reader = new Store(Database)) all = (await reader.ReadAsync()).ToList();
        var victim = all.First(r => r.Kind == "lcm");
        Damage("UPDATE RecordHeaders SET ClassId = ClassId + 1 WHERE hex(Id) = $hex", victim.Id);
        await using var certified = new Store(Database);
        var refused = await Assert.ThrowsAsync<IndexUnavailableException>(() => certified.ReferencesAsync());
        Assert.Contains("RecordHeaders", refused.Message);
        Damage("DELETE FROM RecordHeaders WHERE hex(Id) = $hex", victim.Id);
        await Assert.ThrowsAsync<IndexUnavailableException>(() => certified.ReferencesAsync());
        Assert.Equal(0, certified.PointAcquisitions);
    }

    [Fact]
    public async Task ADamagedEdgeTableTakesTheFullPathAndIsNeverReadAsAnEmptyAnswer()
    {
        var seed = await Import();
        Damage("DELETE FROM IncomingReferences");
        await using var damaged = new Store(Database);
        var index = await damaged.ReferencesAsync();
        Assert.Equal(0, damaged.CertifiedAcquisitions);
        Assert.Null(index.Incoming([seed.FirstEntryId]).NativeTotal);
        Assert.False(index.DeletionImpact(seed.FirstEntryId).NativeComplete);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new Store(Database).VerifyAsync());
        Assert.Contains("IncomingReferences", failure.Message);
    }

    [Fact]
    public async Task CancellingAnAcquisitionMidLoadLeavesTheHandleReusable()
    {
        var seed = await Import();
        await using var store = Store.OpenPublishedReader(Database);
        foreach (var stage in new[] { "headers", "edges", "index", "schema" })
        {
            using var cancel = new CancellationTokenSource();
            store.AcquisitionStage = reached => { if (reached == stage) cancel.Cancel(); };
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.VerifiedPointAsync(cancel.Token));
            Assert.Equal(0, store.PointAcquisitions);
        }
        store.AcquisitionStage = null;
        var index = await store.ReferencesAsync();
        Assert.Equal(1, store.CertifiedAcquisitions);
        Assert.True(index.Incoming([seed.FirstEntryId]).Native.Length > 0);
    }

    [Fact]
    public async Task ALazyPointReadAfterAnotherHandlePublishedRefusesWithTheTypedRevisionRefusal()
    {
        var seed = await Import();
        await using var reader = new Store(Database);
        var held = await reader.VerifiedPointAsync(default);
        Assert.NotNull(await held.Records.GetAsync(seed.FirstEntryId, default));
        await using (var writer = new Store(Database))
        {
            var (revision, old) = await Current(writer, seed.FirstEntryId);
            await Bump(writer, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
        }
        await Assert.ThrowsAsync<Store.StoreRevisionUnavailableException>(() => held.Records.GetAsync(seed.FirstEntryId, default));
        await Assert.ThrowsAsync<Store.StoreRevisionUnavailableException>(() => held.Records.GetManyAsync([seed.FirstEntryId], default));
        // The reader's next query takes a fresh point instead.
        Assert.NotEqual(held.State.Revision, (await reader.ReferencesAsync()).Revision);
    }

    [Fact]
    public async Task AFirstWriteOnACertifiedPointUsesItWithoutDecodingTheStoreAndKeepsTheAnswersEqual()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var (revision, old) = await Current(store, seed.FirstEntryId);
        var decodedBefore = store.SnapshotsDecoded;
        var next = await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
        Assert.Equal(1, store.CertifiedAcquisitions);
        Assert.True(store.SnapshotsDecoded - decodedBefore < 100);
        await store.VerifyAsync();
        await using var fresh = new Store(Database);
        Assert.Equal(next, (await fresh.ReferencesAsync()).Revision);
        Assert.Equal(1, fresh.CertifiedAcquisitions);
        var again = await Bump(store, seed.FirstEntryId, next, JsonSerializer.SerializeToElement(old.GetInt32() + 1), old.GetInt32() + 2);
        await using var last = new Store(Database);
        Assert.Equal(again, (await last.ReferencesAsync()).Revision);
        Assert.Equal((await store.PlanningIndexAsync()).DeletionImpact(seed.FirstEntryId).PlanToken,
            (await last.PlanningIndexAsync()).DeletionImpact(seed.FirstEntryId).PlanToken);
    }

    [Fact]
    public async Task AReadOnlyReaderFetchesContentRowsByIdAndChecksThemAgainstTheirIdentity()
    {
        await Import();
        await using (var certified = Store.OpenPublishedReader(Database))
        {
            await certified.ReferencesAsync();
            Assert.Equal(1, certified.CertifiedAcquisitions);
            Assert.InRange(certified.DemandedContentRows, 1, 5000);
        }
        // A content row that no longer hashes to its identity is refused when a keyed read consumes it.
        List<GenericRecord> all;
        await using (var reader = new Store(Database)) all = (await reader.ReadAsync()).ToList();
        Damage("UPDATE RecordContentIdentity SET Id = Id + 1000000");
        await using var damaged = Store.OpenPublishedReader(Database);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => damaged.ReferencesAsync());
        Assert.Contains("RecordContent", refused.Message);
        Assert.Equal(0, damaged.PointAcquisitions);
    }

    [Fact]
    public async Task ALazyReferenceIndexBuiltByConcurrentFirstUsersGivesOneResult()
    {
        var ids = Enumerable.Range(0, 3000).Select(n => Guid.NewGuid()).ToArray();
        var headers = ids.Select((id, n) => new ReferenceRecord(id, 1, null, 0)).ToArray();
        var edges = Enumerable.Range(0, 20000).Select(n => new NativeReference(ids[n % ids.Length], 7, n / ids.Length, ids[(n * 31) % ids.Length], false)).ToArray();
        var expected = new ReferenceIndexSnapshot("r", edges, headers, []).Incoming(ids.Take(400)).Native;
        for (var round = 0; round < 40; round++)
        {
            var index = new ReferenceIndexSnapshot("r", edges, headers, []);
            Assert.False(index.IndexBuilt);
            using var gate = new Barrier(16);
            var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            {
                gate.SignalAndWait();
                return index.Incoming(ids.Take(400)).Native;
            })));
            Assert.All(results, result => Assert.Equal(expected.ToArray(), result.ToArray()));
        }
    }
}

/// <summary>The private Indonesian copy: a certified point answers what a full point answers, for every native record.</summary>
public sealed class CertifiedPointIndonesianTests(ImportedProject project) : IClassFixture<ImportedProject>
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task ACertifiedPointAnswersEqualAFullPointForEveryDeletableRecord()
    {
        var path = project.CopyStore();
        await using var full = new Store(path) { CertifiedAcquisitionAllowed = false };
        await full.VerifyAsync();
        var records = await full.ReadAsync();
        var eager = await full.ReferencesAsync();
        await using var certified = new Store(path);
        var lazy = await certified.ReferencesAsync();
        Assert.Equal(1, certified.CertifiedAcquisitions);
        Assert.Equal(eager.Revision, lazy.Revision);
        var targets = records.Where(r => r.Kind == "lcm" && eager.UsageSchema!.IsDeletable(r.ClassId)).Select(r => r.Id).ToArray();
        Assert.NotEmpty(targets);
        foreach (var id in targets)
        {
            var expected = eager.DeletionImpact(id);
            var actual = lazy.DeletionImpact(id);
            Assert.Equal(expected.PlanToken, actual.PlanToken);
            Assert.Equal(expected.Links.ToArray(), actual.Links.ToArray());
            Assert.Equal(expected.Analyses.ToArray(), actual.Analyses.ToArray());
        }
    }
}
