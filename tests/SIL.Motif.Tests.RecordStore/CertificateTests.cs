using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.LCModel;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>The store certificate: trusted cold writes, crash flag, tampering and persisted content identity.</summary>
public sealed partial class DeletionSafetyTests
{
    private static readonly int Homograph = LexEntryTags.kflidHomographNumber;

    private sealed class Crash : Exception;

    private SqliteConnection Raw(string? path = null)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path ?? Database, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private object? Scalar(string sql, string? path = null)
    {
        using var connection = Raw(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void Run(string sql, string? path = null)
    {
        using var connection = Raw(path);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>Flips one RecordContent row's last byte, never a no-op.</summary>
    private void DamageOneContentRow()
    {
        const string pick = "SELECT Id FROM RecordContent WHERE substr(Data, 1, 1) = x'00' AND length(Data) > 4 ORDER BY Id LIMIT 1";
        using var connection = Raw();
        string Hex(string sql)
        {
            using var read = connection.CreateCommand();
            read.CommandText = sql;
            return Convert.ToHexString((byte[])read.ExecuteScalar()!);
        }
        var before = Hex($"SELECT Data FROM RecordContent WHERE Id = ({pick})");
        using var update = connection.CreateCommand();
        update.CommandText = $"UPDATE RecordContent SET Data = substr(Data, 1, length(Data) - 1) || CASE WHEN substr(Data, length(Data), 1) = x'01' THEN x'00' ELSE x'01' END WHERE Id = ({pick})";
        Assert.Equal(1, update.ExecuteNonQuery());
        var after = Hex($"SELECT Data FROM RecordContent WHERE Id = ({pick})");
        Assert.NotEqual(before, after);
    }

    private async Task<(string Revision, JsonElement Value)> Current(Store store, Guid entry)
    {
        var revision = (await store.ReferencesAsync()).Revision;
        var value = (await store.ReadAsync()).Single(r => r.Id == entry).Values[Homograph.ToString()];
        return (revision, value);
    }

    private static Task<string> Bump(Store store, Guid entry, string revision, JsonElement old, int to) =>
        store.SetFieldAsync(entry, Homograph, old, JsonSerializer.SerializeToElement(to), revision, Guid.NewGuid());

    [Fact]
    public async Task AColdWriteOnACertifiedStoreExtendsTheStoredReplayDigestInsteadOfScanning()
    {
        var seed = await Import();
        await using (var first = new Store(Database))
        {
            Assert.Equal(StoreVerification.Certified, first.Verification);
            var (revision, old) = await Current(first, seed.FirstEntryId);
            var moved = await Bump(first, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
            Assert.Equal(0, first.HistoryScans);
            Assert.Equal(StoreVerification.Certified, first.Verification);
            var certificate = await first.CertificateAsync();
            Assert.Equal(moved, certificate!.Revision);
            Assert.Equal("incremental", certificate.Provenance);
            Assert.False(certificate.WriteInProgress);
            Assert.Equal((await first.ScanHistoryAsync()).Final(), certificate.ReplayDigest);
        }
        await using var second = new Store(Database);
        var (revision2, old2) = await Current(second, seed.FirstEntryId);
        await Bump(second, seed.FirstEntryId, revision2, old2, old2.GetInt32() + 1);
        Assert.Equal(0, second.HistoryScans);
        await second.VerifyAsync();
        Assert.Equal(StoreVerification.Full, second.Verification);
    }

    [Fact]
    public async Task ASetCrashFlagOrADamagedOrForeignCertificateForcesTheFullScanAndRecertifies()
    {
        var seed = await Import();
        var other = Path.Combine(root, "other.sqlite");
        await using (var foreign = new Store(other))
        {
            var record = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
            await foreign.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
        }
        var foreignId = Scalar("SELECT StoreId FROM RecordStoreCertificate", other);
        var ownId = Scalar("SELECT StoreId FROM RecordStoreCertificate");
        Assert.NotEqual(ownId, foreignId);
        foreach (var damage in new[]
        {
            "UPDATE RecordStoreCertificate SET WriteInProgress = 1",
            "UPDATE RecordStoreCertificate SET ReplayDigest = 'damaged'",
            $"UPDATE RecordStoreCertificate SET StoreId = '{foreignId}'"
        })
        {
            Run(damage);
            await using var store = new Store(Database);
            Assert.Equal(StoreVerification.Uncertified, store.Verification);
            var (revision, old) = await Current(store, seed.FirstEntryId);
            await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
            Assert.Equal(1, store.HistoryScans);
            Assert.Equal(StoreVerification.Full, store.Verification);
            var certificate = (await store.CertificateAsync())!;
            Assert.Equal("full-verify", certificate.Provenance);
            Assert.False(certificate.WriteInProgress);
            Assert.Equal(ownId, certificate.StoreId);
        }
    }

    [Theory]
    [InlineData(WriteStage.HistoryWritten)]
    [InlineData(WriteStage.StateWritten)]
    [InlineData(WriteStage.Committed)]
    public async Task ACrashAtAnyWriteStageReopensAtTheOldOrTheNewPointNeverAMismatch(WriteStage stage)
    {
        var seed = await Import();
        string oldRevision;
        await using (var crashing = new Store(Database))
        {
            var (revision, old) = await Current(crashing, seed.FirstEntryId);
            oldRevision = revision;
            crashing.WriteStageReached = reached => { if (reached == stage) throw new Crash(); };
            await Assert.ThrowsAsync<Crash>(() => Bump(crashing, seed.FirstEntryId, revision, old, old.GetInt32() + 5));
        }
        var state = (string)Scalar("SELECT Revision FROM RecordStoreState")!;
        Assert.Equal(state, Scalar("SELECT Revision FROM RecordStoreCertificate"));
        var committed = stage == WriteStage.Committed;
        Assert.Equal(committed, state != oldRevision);
        Assert.Equal(committed ? 0L : 1L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
        await using var reopened = new Store(Database);
        Assert.Equal(committed ? StoreVerification.Certified : StoreVerification.Uncertified, reopened.Verification);
        var (now, value) = await Current(reopened, seed.FirstEntryId);
        Assert.Equal(state, now);
        await Bump(reopened, seed.FirstEntryId, now, value, value.GetInt32() + 1);
        Assert.Equal(committed ? 0 : 1, reopened.HistoryScans);
        await reopened.VerifyAsync();
    }

    [Theory]
    [InlineData("state", "UPDATE RecordStoreState SET SnapshotDigest = 'damaged'", "RecordStoreState")]
    [InlineData("certificate", "UPDATE RecordStoreCertificate SET SnapshotDigest = 'damaged'", "RecordStoreCertificate")]
    [InlineData("change", "UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)", "ChangeEntities")]
    [InlineData("content", "", "RecordContent")]
    public async Task DamageToAnyOneTableRefusesAndVerifyNamesIt(string kind, string damage, string table)
    {
        await Import();
        await using (var store = new Store(Database))
        {
            await store.VerifyAsync();
            if (kind == "content") DamageOneContentRow(); else Run(damage);
            var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
            Assert.Contains(table, failure.Message);
        }
        if (kind == "content")
        {
            Assert.Throws<InvalidDataException>(() => new Store(Database));
            return;
        }
        await using var reopened = new Store(Database);
        Assert.NotEqual(StoreVerification.Full, reopened.Verification);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => reopened.VerifyAsync());
        Assert.Contains(table, refused.Message);
        if (kind == "state") await Assert.ThrowsAsync<InvalidDataException>(() => reopened.ReadAsync());
    }

    [Fact]
    public async Task ACertifiedWriteDoesNotRescanButTheDamageStaysVisibleToVerify()
    {
        var seed = await Import();
        Run("UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)");
        await using var store = new Store(Database);
        var (revision, old) = await Current(store, seed.FirstEntryId);
        await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
        Assert.Equal(0, store.HistoryScans);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
        Assert.Contains("ChangeEntities", failure.Message);
        Run("UPDATE RecordStoreCertificate SET WriteInProgress = 1");
        await using var forced = new Store(Database);
        var (revision2, old2) = await Current(forced, seed.FirstEntryId);
        await Assert.ThrowsAsync<InvalidDataException>(() => Bump(forced, seed.FirstEntryId, revision2, old2, old2.GetInt32() + 1));
    }

    [Fact]
    public async Task TheFirstContentWriteFindsIdentitiesInTheStoreAndNeverBuildsTheDictionary()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        var rows = (long)Scalar("SELECT count(*) FROM RecordContent")!;
        var (revision, old) = await Current(store, seed.FirstEntryId);
        var value = old.GetInt32();
        revision = await Bump(store, seed.FirstEntryId, revision, old, value + 1000);
        Assert.Equal(0, store.ContentIdentityDictionaryBuilds);
        var added = (long)Scalar("SELECT count(*) FROM RecordContent")! - rows;
        Assert.InRange(added, 1, 3);
        Assert.Equal(Scalar("SELECT count(*) FROM RecordContent"), Scalar("SELECT count(*) FROM RecordContentIdentity"));
        // Writing a value that an earlier committed write stored reuses its row, found through the table.
        revision = await Bump(store, seed.FirstEntryId, revision, JsonSerializer.SerializeToElement(value + 1000), value + 2000);
        var stored = (long)Scalar("SELECT count(*) FROM RecordContent")!;
        await Bump(store, seed.FirstEntryId, revision, JsonSerializer.SerializeToElement(value + 2000), value + 1000);
        Assert.Equal(stored, (long)Scalar("SELECT count(*) FROM RecordContent")!);
        Assert.Equal(Scalar("SELECT count(*) FROM RecordContent"), Scalar("SELECT count(*) FROM RecordContentIdentity"));
        Assert.Equal(0, store.ContentIdentityDictionaryBuilds);
        await store.VerifyAsync();
    }

    [Fact]
    public async Task AnotherHandleOpenedAfterTheFirstCommittedSeesItsCertificate()
    {
        var seed = await Import();
        string revision;
        await using (var a = new Store(Database))
        {
            var (now, old) = await Current(a, seed.FirstEntryId);
            revision = await Bump(a, seed.FirstEntryId, now, old, old.GetInt32() + 1);
        }
        await using var b = new Store(Database);
        Assert.Equal(StoreVerification.Certified, b.Verification);
        var certificate = (await b.CertificateAsync())!;
        Assert.Equal(revision, certificate.Revision);
        var (now2, old2) = await Current(b, seed.FirstEntryId);
        await Bump(b, seed.FirstEntryId, now2, old2, old2.GetInt32() + 1);
        Assert.Equal(0, b.HistoryScans);
    }

    [Fact]
    public async Task AfterRandomWritesAcrossReopensTheIncrementalDigestsEqualAFullRescan()
    {
        var seed = await Import();
        var random = new Random(11);
        var store = new Store(Database);
        try
        {
            var (revision, old) = await Current(store, seed.FirstEntryId);
            var value = old.GetInt32();
            for (var i = 0; i < 100; i++)
            {
                if (random.Next(10) == 0)
                {
                    await store.DisposeAsync();
                    store = new Store(Database);
                    revision = (await store.ReferencesAsync()).Revision;
                }
                var next = value + random.Next(1, 9);
                revision = await Bump(store, seed.FirstEntryId, revision, JsonSerializer.SerializeToElement(value), next);
                value = next;
            }
            var certificate = (await store.CertificateAsync())!;
            var scanned = await store.ScanHistoryAsync();
            Assert.Equal(scanned.Final(), certificate.ReplayDigest);
            Assert.Equal(scanned.CommitRows.ToText(), certificate.CommitRows);
            Assert.Equal(scanned.ChangeRows.ToText(), certificate.ChangeRows);
            Assert.Equal(revision, certificate.Revision);
            await store.VerifyAsync();
        }
        finally { await store.DisposeAsync(); }
    }

    // Moves the first item of the first stored list that has two, so the operation is real and reversible.
    private static async Task<ListRecordOp> SwapFirstList(Store store)
    {
        foreach (var record in await store.ReadAsync())
            foreach (var (key, value) in record.Values)
                if (value.ValueKind == JsonValueKind.Array && value.GetArrayLength() >= 2 && RecordInitialization.Type(record, key) is >= 26 and <= 28
                    && ListAuthoring.Move(record, int.Parse(key), Guid.NewGuid(), 0, 1) is { } move)
                    return move;
        throw new InvalidOperationException("The seed has no list with two items.");
    }

    [Fact]
    public async Task ANoOpWriteLeavesNoWriteStateBehindAndTheNextWriteStillWorks()
    {
        var seed = await Import();
        await using (var store = new Store(Database))
        {
            var (revision, old) = await Current(store, seed.FirstEntryId);
            Assert.Equal(revision, await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32()));
            Assert.Equal(7L, Scalar("PRAGMA user_version"));
            Assert.Equal(0L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
            var moved = await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
            Assert.NotEqual(revision, moved);
            Assert.Equal(0L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
            await store.VerifyAsync();
        }
        Assert.Equal(7L, Scalar("PRAGMA user_version"));
        await using var reopened = new Store(Database);
        Assert.Equal(StoreVerification.Certified, reopened.Verification);
    }

    [Fact]
    public async Task ATamperedCertificateOnTamperedHistoryIsNeverRecertified()
    {
        var seed = await Import();
        Run("UPDATE RecordStoreCertificate SET ReplayDigest = 'damaged'");
        Run("UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)");
        await using var store = new Store(Database);
        Assert.Equal(StoreVerification.Uncertified, store.Verification);
        var (revision, old) = await Current(store, seed.FirstEntryId);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1));
        Assert.Contains("History is corrupt", failure.Message);
        Assert.Equal("damaged", Scalar("SELECT ReplayDigest FROM RecordStoreCertificate"));
        Assert.Equal(0L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
        Assert.Equal(revision, Scalar("SELECT Revision FROM RecordStoreState"));
    }

    [Fact]
    public async Task VerifyNamesTheStateRowWhenItsRevisionIsDamaged()
    {
        await Import();
        await using var store = new Store(Database);
        Run("UPDATE RecordStoreState SET Revision = 'damaged'");
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
        Assert.Contains("RecordStoreState", failure.Message);
    }

    [Fact]
    public async Task VerifyComparesTheWholeIdentityTableAndRebuildRepairsIt()
    {
        var seed = await Import();
        await using var store = new Store(Database);
        await store.VerifyAsync();
        Run("UPDATE RecordContentIdentity SET Id = Id + 1000000 WHERE Identity = (SELECT Identity FROM RecordContentIdentity LIMIT 1)");
        var rows = Scalar("SELECT count(*) FROM RecordContentIdentity");
        Assert.Equal(Scalar("SELECT count(*) FROM RecordContent"), rows);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
        Assert.Contains("RecordContentIdentity", failure.Message);
        await store.RebuildContentIdentitiesAsync();
        await store.VerifyAsync();
        var (revision, old) = await Current(store, seed.FirstEntryId);
        await Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1);
    }

    [Fact]
    public async Task AHandleThatReacquiresAfterAnotherWritesStopsReportingWhatItDidNotVerify()
    {
        var seed = await Import();
        IReadOnlyList<GenericRecord> records;
        await using (var peek = new Store(Database)) records = await peek.ReadAsync();
        var bundle = records.First(r => r.ClassId == WfiMorphBundleTags.kClassId &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].ValueKind == JsonValueKind.String &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].GetGuid() == seed.FirstSenseId);
        await using var watcher = new Store(Database);
        await watcher.VerifyAsync();
        Assert.Equal(StoreVerification.Full, watcher.Verification);
        await using (var writer = new Store(Database))
        {
            var revision = (await writer.ReferencesAsync()).Revision;
            await writer.SetFieldAsync(bundle.Id, WfiMorphBundleTags.kflidSense, JsonSerializer.SerializeToElement(seed.FirstSenseId),
                JsonSerializer.SerializeToElement(seed.SecondSenseId), revision, Guid.NewGuid());
        }
        await watcher.ReferencesAsync();
        Assert.Equal(StoreVerification.Certified, watcher.Verification);
    }

    [Theory]
    [InlineData(WriteStage.HistoryWritten)]
    [InlineData(WriteStage.StateWritten)]
    [InlineData(WriteStage.Committed)]
    public async Task AListOperationIsOneTransactionSoACrashAtAnyStageLeavesTheOldPointOrTheNewNeverAGap(WriteStage stage)
    {
        await Import();
        string oldRevision;
        await using (var crashing = new Store(Database))
        {
            oldRevision = (await crashing.ReferencesAsync()).Revision;
            var swap = await SwapFirstList(crashing);
            crashing.WriteStageReached = reached => { if (reached == stage) throw new Crash(); };
            await Assert.ThrowsAsync<Crash>(() => crashing.CommitListOperationsAsync([swap], Guid.NewGuid()));
        }
        var committed = stage == WriteStage.Committed;
        var state = (string)Scalar("SELECT Revision FROM RecordStoreState")!;
        Assert.Equal(committed, state != oldRevision);
        // History, state row, certificate and binding always agree.
        Assert.Equal(state, Scalar("SELECT Revision FROM RecordStoreCertificate"));
        Assert.Equal(state, Scalar("SELECT Revision FROM RecordPublicationBinding"));
        Assert.Equal(0L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
        await using var reopened = new Store(Database);
        Assert.Equal(StoreVerification.Certified, reopened.Verification);
        Assert.Equal(state, (await reopened.ReferencesAsync()).Revision);
        await reopened.VerifyAsync();
        await reopened.CommitListOperationsAsync([await SwapFirstList(reopened)], Guid.NewGuid());
        await reopened.VerifyAsync();
    }

    [Fact]
    public async Task AListOperationRefusesHistoryThatNoLongerMatchesTheStoredDigestInsteadOfCertifyingIt()
    {
        var seed = await Import();
        Run("UPDATE ChangeEntities SET EntityId = randomblob(16) WHERE rowid = (SELECT min(rowid) FROM ChangeEntities)");
        await using var store = new Store(Database);
        var swap = await SwapFirstList(store);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.CommitListOperationsAsync([swap], Guid.NewGuid()));
        Assert.Contains("History is corrupt", failure.Message);
        Assert.Equal(0L, Scalar("SELECT count(*) FROM RecordStoreCertificate WHERE Provenance = 'full-verify' AND WriteInProgress = 1"));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
    }

    [Fact]
    public async Task MixedWritesAcrossHandlesKeepTheIncrementalDigestsEqualToAFullRescan()
    {
        var seed = await Import();
        IReadOnlyList<GenericRecord> records;
        await using (var peek = new Store(Database)) records = await peek.ReadAsync();
        var entries = records.Where(r => r.ClassId == LexEntryTags.kClassId &&
            r.Values.TryGetValue(Homograph.ToString(), out var h) && h.ValueKind == JsonValueKind.Number).Take(4).Select(r => r.Id).ToArray();
        Assert.True(entries.Length >= 2);
        var bundle = records.First(r => r.ClassId == WfiMorphBundleTags.kClassId &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].ValueKind == JsonValueKind.String &&
            r.Values[WfiMorphBundleTags.kflidSense.ToString()].GetGuid() == seed.FirstSenseId).Id;
        var sense = seed.FirstSenseId;
        var random = new Random(23);
        var value = entries.ToDictionary(id => id, id => records.Single(r => r.Id == id).Values[Homograph.ToString()].GetInt32());
        var stored = value.Values.ToList();
        Store? store = new Store(Database);
        try
        {
            for (var i = 0; i < 100; i++)
            {
                var revision = (await store.ReferencesAsync()).Revision;
                switch (random.Next(6))
                {
                    case 0:
                        await store.DisposeAsync();
                        store = new Store(Database);
                        break;
                    case 1:
                        var other = sense == seed.FirstSenseId ? seed.SecondSenseId : seed.FirstSenseId;
                        await store.SetFieldAsync(bundle, WfiMorphBundleTags.kflidSense, JsonSerializer.SerializeToElement(sense),
                            JsonSerializer.SerializeToElement(other), revision, Guid.NewGuid());
                        sense = other;
                        break;
                    case 2:
                        var same = entries[random.Next(entries.Length)];
                        Assert.Equal(revision, await store.SetFieldAsync(same, Homograph, JsonSerializer.SerializeToElement(value[same]),
                            JsonSerializer.SerializeToElement(value[same]), revision, Guid.NewGuid()));
                        break;
                    case 4:
                        // Another handle writes and closes; this one must reacquire.
                        await using (var second = new Store(Database))
                        {
                            var id = entries[random.Next(entries.Length)];
                            var to = random.Next(2) == 0 && stored.Count > 0 ? stored[random.Next(stored.Count)] : value[id] + random.Next(1, 9);
                            if (to != value[id])
                            {
                                var current = (await second.ReferencesAsync()).Revision;
                                await second.SetFieldAsync(id, Homograph, JsonSerializer.SerializeToElement(value[id]),
                                    JsonSerializer.SerializeToElement(to), current, Guid.NewGuid());
                                value[id] = to;
                                stored.Add(to);
                            }
                        }
                        await store.DisposeAsync();
                        store = new Store(Database);
                        break;
                    default:
                        var target = entries[random.Next(entries.Length)];
                        var next = random.Next(2) == 0 && stored.Count > 0 ? stored[random.Next(stored.Count)] : value[target] + random.Next(1, 9);
                        if (next == value[target]) break;
                        await store.SetFieldAsync(target, Homograph, JsonSerializer.SerializeToElement(value[target]),
                            JsonSerializer.SerializeToElement(next), revision, Guid.NewGuid());
                        value[target] = next;
                        stored.Add(next);
                        break;
                }
                var certificate = (await store.CertificateAsync())!;
                var scanned = await store.ScanHistoryAsync();
                Assert.Equal(scanned.Final(), certificate.ReplayDigest);
                Assert.Equal(scanned.CommitRows.ToText(), certificate.CommitRows);
                Assert.Equal(scanned.ChangeRows.ToText(), certificate.ChangeRows);
                Assert.False(certificate.WriteInProgress);
                // Incremental digests must equal an independent handle's rescan at every step.
                await using var rescan = Store.OpenPublishedReader(Database);
                var fresh = await rescan.RescanDigestsAsync();
                Assert.Equal(fresh.Snapshot, Scalar("SELECT SnapshotDigest FROM RecordStoreState"));
                Assert.Equal(fresh.Index, Scalar("SELECT IndexDigest FROM RecordStoreState"));
                Assert.Equal(fresh.Content, Scalar("SELECT ContentDigest FROM RecordStoreState"));
                Assert.Equal(fresh.Snapshot, certificate.SnapshotDigest);
                Assert.Equal(fresh.Index, certificate.IndexDigest);
                Assert.Equal(fresh.Content, certificate.ContentDigest);
            }
            await store.VerifyAsync();
            // A list operation republishes everything; the store is then certified again and still equals a full rescan.
            await store.CommitListOperationsAsync([await SwapFirstList(store)], Guid.NewGuid());
            var final = (await store.CertificateAsync())!;
            Assert.Equal((await store.ScanHistoryAsync()).Final(), final.ReplayDigest);
            Assert.Equal("full-verify", final.Provenance);
            await store.DisposeAsync();
            store = new Store(Database);
            Assert.Equal(StoreVerification.Certified, store.Verification);
            await store.VerifyAsync();
        }
        finally { await store.DisposeAsync(); }
    }

    [Fact]
    public async Task VerifyRefusesToRecertifyWhenAnotherHandleCommittedHistoryItNeverPublished()
    {
        var seed = await Import();
        await using var verifier = new Store(Database);
        verifier.AfterVerifyScan = async () =>
        {
            // Another process commits history and dies before publishing it (here, by committing through Harmony alone).
            await using var other = new Store(Database);
            var swap = await SwapFirstList(other);
            await other.Model.AddChange(Guid.NewGuid(), swap);
        };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => verifier.VerifyAsync());
        Assert.Contains("not published", failure.Message);
        Assert.NotEqual(StoreVerification.Full, verifier.Verification);
    }

    [Theory]
    [InlineData("no-op")]
    [InlineData("stale revision")]
    [InlineData("stale prior value")]
    public async Task AColdHandleWhoseFirstWriteChangesNothingStillReportsCertified(string kind)
    {
        var seed = await Import();
        string revision;
        JsonElement old;
        await using (var other = new Store(Database)) (revision, old) = await Current(other, seed.FirstEntryId);
        await using var cold = new Store(Database);
        Assert.Equal(StoreVerification.Certified, cold.Verification);
        var number = old.GetInt32();
        var attempt = kind switch
        {
            "no-op" => Bump(cold, seed.FirstEntryId, revision, old, number),
            "stale revision" => Bump(cold, seed.FirstEntryId, "stale", old, number + 1),
            _ => Bump(cold, seed.FirstEntryId, revision, JsonSerializer.SerializeToElement(number + 5), number + 1)
        };
        if (kind == "no-op") Assert.Equal(revision, await attempt);
        else await Assert.ThrowsAnyAsync<InvalidOperationException>(() => attempt);
        Assert.Equal(StoreVerification.Certified, cold.Verification);
        Assert.Equal(0L, Scalar("SELECT WriteInProgress FROM RecordStoreCertificate"));
        // The next real write extends the certificate without a scan, as a certified handle would.
        var moved = await Bump(cold, seed.FirstEntryId, revision, old, number + 1);
        Assert.NotEqual(revision, moved);
        Assert.Equal(0, cold.HistoryScans);
        Assert.Equal(StoreVerification.Certified, cold.Verification);
    }
}
