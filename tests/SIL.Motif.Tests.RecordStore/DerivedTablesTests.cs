using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Harmony.Changes;
using SIL.Motif.RecordStore;
using Xunit;
using SIL.Motif.Tests.RecordStore.HandAnalyses;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

/// <summary>Format 7: headers, chunk digests and the publication binding, on the seeded native project.</summary>
public sealed partial class DeletionSafetyTests
{
    // The derived tables must agree with the state row, read raw so no handle can mask damage.
    private void AssertDerivedConsistent(string? path = null)
    {
        using var connection = Raw(path);
        string One(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return (string)command.ExecuteScalar()!;
        }
        var chunks = new SetDigest();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Digest, Count FROM RecordChunks";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                Assert.True(SetDigest.TryParse(reader.GetString(0), out var digest));
                Assert.Equal(digest.Count, reader.GetInt64(1));
                chunks = chunks.Combine(digest);
            }
        }
        Assert.Equal(One("SELECT SnapshotDigest FROM RecordStoreState"), chunks.Final("snapshot"));
        foreach (var column in new[] { "Revision", "SnapshotDigest", "ReplayDigest", "ContentDigest", "IndexDigest" })
            Assert.Equal(One($"SELECT {column} FROM RecordStoreState"), One($"SELECT {column} FROM RecordPublicationBinding"));
    }

    private List<(long Chunk, string Digest)> ChunkRows()
    {
        using var connection = Raw();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ChunkId, Digest FROM RecordChunks ORDER BY ChunkId";
        using var reader = command.ExecuteReader();
        var rows = new List<(long, string)>();
        while (reader.Read()) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    [Fact]
    public async Task ChunkDigestsSumToTheStateDigestAfterImportRandomWritesListOperationsRebuildAndRegenerate()
    {
        var seed = await Import();
        AssertDerivedConsistent();
        Assert.True((long)Scalar("SELECT count(*) FROM RecordChunks")! > 1);
        var random = new Random(31);
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
            AssertDerivedConsistent();
            // The incremental chunk rows are exactly what a full rebuild computes.
            var incremental = ChunkRows();
            await store.VerifyAsync();
            await store.RebuildReferenceIndexAsync();
            Assert.Equal(incremental, ChunkRows());
            AssertDerivedConsistent();
            await store.CommitListOperationsAsync([await SwapFirstList(store)], Guid.NewGuid());
            AssertDerivedConsistent();
            await store.VerifyAsync();
            var afterList = ChunkRows();
            await store.RegenerateAsync();
            Assert.Equal(afterList, ChunkRows());
            AssertDerivedConsistent();
            await store.VerifyAsync();
        }
        finally { await store.DisposeAsync(); }
    }

    [Fact]
    public async Task TheRangedLatestSnapshotReadEqualsTheFullReadPerChunkIncludingEditedLists()
    {
        await Import();
        await using (var editor = new Store(Database))
            await editor.CommitListOperationsAsync([await SwapFirstList(editor)], Guid.NewGuid());
        await using var store = new Store(Database);
        var full = await store.ReadAsync();
        var seen = 0;
        for (var chunk = 0; chunk < Store.ChunkCount; chunk++)
        {
            var expected = full.Where(r => Store.ChunkOf(r.Id) == chunk).ToArray();
            var ranged = await store.ReadChunkUnverifiedAsync(chunk);
            Assert.Equal(expected.Select(r => r.Id), ranged.Select(r => r.Id));
            Assert.Equal(expected.Select(Store.SnapshotItem), ranged.Select(Store.SnapshotItem));
            var verified = await store.ReadChunkAsync(chunk);
            Assert.Equal(expected.Select(Store.SnapshotItem), verified.Select(Store.SnapshotItem));
            seen += expected.Length;
        }
        Assert.Equal(full.Count, seen);
        var target = full[full.Count / 2];
        Assert.Equal(Store.SnapshotItem(target), Store.SnapshotItem((await store.ReadRecordAsync(target.Id))!));
        Assert.Null(await store.ReadRecordAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task HeadersEqualTheDecodedRecordsAndAReadOnlyHandleServesKeyedReads()
    {
        await Import();
        IReadOnlyList<GenericRecord> full;
        await using (var store = new Store(Database)) full = await store.ReadAsync();
        await using var reader = Store.OpenPublishedReader(Database);
        var headers = await reader.ReadHeadersAsync();
        Assert.Equal(full.Count, headers.Count);
        var byId = headers.ToDictionary(h => h.Id);
        Assert.All(full, r =>
        {
            var header = byId[r.Id];
            Assert.Equal((r.Kind, r.ClassId, r.Owner, r.OwningField, false), (header.Kind, header.ClassId, header.Owner, header.OwningField, header.Deleted));
        });
        var record = full.First(r => r.Owner is not null);
        Assert.Equal(Store.SnapshotItem(record), Store.SnapshotItem((await reader.ReadRecordAsync(record.Id))!));
    }

    public static TheoryData<string, string, string> Tampering() => new()
    {
        { "chunk row", "UPDATE RecordChunks SET Digest = '00000000000000000000000000000000:00000000000000000000000000000000:' || Count WHERE ChunkId = $chunk", "RecordChunks" },
        { "chunk row deleted", "DELETE FROM RecordChunks WHERE ChunkId = $chunk", "RecordChunks" },
        { "snapshot blob", "UPDATE Snapshots SET Entity = (SELECT Entity FROM Snapshots WHERE EntityId <> (SELECT EntityId FROM Snapshots WHERE hex(EntityId) = $hex) LIMIT 1) WHERE hex(EntityId) = $hex", "Snapshots" },
        { "headers row", "UPDATE RecordHeaders SET ClassId = ClassId + 1 WHERE hex(Id) = $hex", "RecordHeaders" },
        { "headers row deleted", "DELETE FROM RecordHeaders WHERE hex(Id) = $hex", "RecordHeaders" },
        { "binding row", "UPDATE RecordPublicationBinding SET SnapshotDigest = 'damaged'", "RecordPublicationBinding" }
    };

    [Theory]
    [MemberData(nameof(Tampering))]
    public async Task DamageToADerivedTableRefusesTheConsumedChunkNamesItAndLeavesOtherChunksReadable(string kind, string damage, string table)
    {
        var seed = await Import();
        Guid target = seed.FirstEntryId, other;
        await using (var store = new Store(Database))
        {
            await store.VerifyAsync();
            var all = await store.ReadAsync();
            var chunk = Store.ChunkOf(target);
            other = all.First(r => Store.ChunkOf(r.Id) != chunk).Id;
            using var connection = Raw();
            using var command = connection.CreateCommand();
            command.CommandText = damage;
            command.Parameters.AddWithValue("$chunk", chunk);
            command.Parameters.AddWithValue("$hex", target.ToString("N").ToUpperInvariant());
            Assert.True(command.ExecuteNonQuery() > 0, kind);
        }
        await using var damaged = Store.OpenPublishedReader(Database);
        var refused = await Assert.ThrowsAsync<InvalidDataException>(() => damaged.ReadRecordAsync(target));
        Assert.Contains(table, refused.Message);
        if (table is "RecordPublicationBinding" or "RecordChunks")
        {
            // The binding and the chunk-row sum are checked before any keyed read is trusted, whatever the chunk.
            await Assert.ThrowsAsync<InvalidDataException>(() => damaged.ReadRecordAsync(other));
            Assert.NotEqual(StoreVerification.Certified, damaged.Verification);
        }
        else Assert.NotNull(await damaged.ReadRecordAsync(other));
        await using var verifier = new Store(Database);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => verifier.VerifyAsync());
        Assert.Contains(table, failure.Message);
    }

    [Fact]
    public async Task DamageToTheHeadersDigestRefusesTheBulkHeaderRead()
    {
        await Import();
        Run("UPDATE RecordHeaders SET Kind = Kind || 'x' WHERE Id = (SELECT min(Id) FROM RecordHeaders)");
        await using var reader = Store.OpenPublishedReader(Database);
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadHeadersAsync());
        Assert.Contains("RecordHeaders", failure.Message);
    }

    [Fact]
    public async Task ADamagedContentRowRefusesTheReadOnlyKeyedReadAndTheWritableOpen()
    {
        var seed = await Import();
        DamageOneContentRow();
        Assert.Throws<InvalidDataException>(() => new Store(Database));
        await using var reader = Store.OpenPublishedReader(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadRecordAsync(seed.FirstEntryId));
    }

    [Fact]
    public async Task DamageToTheIdentityTableIsNamedByVerifyAndDoesNotAffectKeyedReads()
    {
        var seed = await Import();
        Run("UPDATE RecordContentIdentity SET Id = Id + 1000000 WHERE Identity = (SELECT Identity FROM RecordContentIdentity LIMIT 1)");
        await using var store = new Store(Database);
        Assert.NotNull(await store.ReadRecordAsync(seed.FirstEntryId));
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync());
        Assert.Contains("RecordContentIdentity", failure.Message);
    }

    [Theory]
    [InlineData(WriteStage.HistoryWritten)]
    [InlineData(WriteStage.StateWritten)]
    [InlineData(WriteStage.Committed)]
    public async Task ACrashAtAnyFieldWriteStageLeavesTheDerivedTablesBoundToTheSurvivingPoint(WriteStage stage)
    {
        var seed = await Import();
        await using (var crashing = new Store(Database))
        {
            var (revision, old) = await Current(crashing, seed.FirstEntryId);
            crashing.WriteStageReached = reached => { if (reached == stage) throw new Crash(); };
            await Assert.ThrowsAsync<Crash>(() => Bump(crashing, seed.FirstEntryId, revision, old, old.GetInt32() + 5));
        }
        AssertDerivedConsistent();
        await using var reopened = new Store(Database);
        await reopened.VerifyAsync();
        Assert.NotNull(await reopened.ReadRecordAsync(seed.FirstEntryId));
    }

    [Fact]
    public async Task AFailureInALaterBatchOfOneListPublicationRollsBackEverything()
    {
        var seed = await Import();
        var commits = Scalar("SELECT count(*) FROM Commits");
        var state = Scalar("SELECT Revision FROM RecordStoreState");
        var batch = Enumerable.Range(0, 500).Select(n => (IChange)new SetRecordValue(seed.FirstEntryId, Homograph.ToString(),
            JsonSerializer.SerializeToElement(n))).Append(new DeleteChange<GenericRecord>(seed.FirstEntryId)).ToArray();
        await using (var store = new Store(Database))
            await Assert.ThrowsAsync<InvalidDataException>(() => store.CommitChangesAsync(batch, Guid.NewGuid()));
        Assert.Equal(commits, Scalar("SELECT count(*) FROM Commits"));
        Assert.Equal(state, Scalar("SELECT Revision FROM RecordStoreState"));
        await using var reopened = new Store(Database);
        Assert.Equal(StoreVerification.Certified, reopened.Verification);
        await reopened.VerifyAsync();
    }

    [Fact]
    public async Task AFieldWriteOnAMismatchedBindingIsRefusedAndNeverRelabelsTheBindingCurrent()
    {
        var seed = await Import();
        Run("UPDATE RecordPublicationBinding SET SnapshotDigest = 'damaged'");
        await using (var store = new Store(Database))
        {
            var (revision, old) = await Current(store, seed.FirstEntryId);
            var refused = await Assert.ThrowsAsync<InvalidDataException>(() => Bump(store, seed.FirstEntryId, revision, old, old.GetInt32() + 1));
            Assert.Contains("RecordPublicationBinding", refused.Message);
            Assert.Contains("RebuildReferenceIndexAsync", refused.Message);
        }
        Assert.Equal("damaged", Scalar("SELECT SnapshotDigest FROM RecordPublicationBinding"));
        await using var reopened = new Store(Database);
        Assert.NotEqual(StoreVerification.Certified, reopened.Verification);
    }

    [Fact]
    public async Task RebuildingTheIndexRepairsDamagedChunkHeaderAndBindingRows()
    {
        var seed = await Import();
        var chunk = Store.ChunkOf(seed.FirstEntryId);
        Run($"UPDATE RecordChunks SET Digest = '00000000000000000000000000000000:00000000000000000000000000000000:' || Count WHERE ChunkId = {chunk}");
        Run("DELETE FROM RecordHeaders WHERE Id = (SELECT min(Id) FROM RecordHeaders)");
        Run("UPDATE RecordPublicationBinding SET SnapshotDigest = 'damaged'");
        await using (var damaged = new Store(Database))
            await Assert.ThrowsAsync<InvalidDataException>(() => damaged.VerifyAsync());
        await using (var repairing = new Store(Database))
            await repairing.RebuildReferenceIndexAsync();
        await using var repaired = new Store(Database);
        await repaired.VerifyAsync();
        AssertDerivedConsistent();
        await using var reader = Store.OpenPublishedReader(Database);
        Assert.NotNull(await reader.ReadRecordAsync(seed.FirstEntryId));
    }

    [Fact]
    public async Task AnEmptyListBatchDoesNotRepublish()
    {
        await Import();
        var state = Scalar("SELECT Revision FROM RecordStoreState");
        await using (var store = new Store(Database))
            await store.CommitListOperationsAsync([], Guid.NewGuid());
        Assert.Equal(state, Scalar("SELECT Revision FROM RecordStoreState"));
    }

    [Fact]
    public async Task KeyedReadsReturnCopiesSoACallerEditNeverReachesTheCheckedChunk()
    {
        var seed = await Import();
        await using var reader = Store.OpenPublishedReader(Database);
        var first = (await reader.ReadRecordAsync(seed.FirstEntryId))!;
        var original = first.Values[Homograph.ToString()];
        first.Values[Homograph.ToString()] = JsonSerializer.SerializeToElement(original.GetInt32() + 77);
        first.DeletedAt = DateTimeOffset.UtcNow;
        var second = (await reader.ReadRecordAsync(seed.FirstEntryId))!;
        Assert.Equal(original.GetInt32(), second.Values[Homograph.ToString()].GetInt32());
        Assert.Null(second.DeletedAt);
        var chunk = (await reader.ReadChunkAsync(Store.ChunkOf(seed.FirstEntryId))).First(r => r.Id == seed.FirstEntryId);
        Assert.Equal(original.GetInt32(), chunk.Values[Homograph.ToString()].GetInt32());
    }

    [Fact]
    public async Task AListOperationRefusesSnapshotsThatNoLongerMatchTheStoredDigestInsteadOfRepublishingThem()
    {
        await Import();
        await using var store = new Store(Database);
        var swap = await SwapFirstList(store);
        // The history is intact; one snapshot row is not what the state row digests.
        Run("UPDATE Snapshots SET Entity = (SELECT Entity FROM Snapshots LIMIT 1 OFFSET 1) WHERE rowid = (SELECT min(rowid) FROM Snapshots)");
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => store.CommitListOperationsAsync([swap], Guid.NewGuid()));
        Assert.True(failure.Message.Contains("snapshot projection is corrupt") || failure.Message.Contains("two latest snapshots decode to"), failure.Message);
        Assert.Equal(0L, Scalar("SELECT count(*) FROM RecordStoreCertificate WHERE Provenance = 'full-verify' AND WriteInProgress = 1"));
    }
}

/// <summary>Format 7 on generic stores built in the test: skewed GUIDs, tombstones and many random chunks.</summary>
public sealed class DerivedTablesGenericTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-derived-" + Guid.NewGuid().ToString("N"));
    private string Database => Path.Combine(root, "records.sqlite");

    private static GenericRecord Generic(Guid id, int n) => new()
    {
        Id = id, Kind = "note", ClassId = 3 + n % 2, Values = { ["text"] = JsonSerializer.SerializeToElement("value " + n) }
    };

    private static Guid InChunk(int chunk, int n)
    {
        var bytes = Guid.NewGuid().ToByteArray(bigEndian: true);
        bytes[0] = (byte)(chunk >> 4);
        bytes[1] = (byte)((bytes[1] & 0x0F) | ((chunk & 15) << 4));
        bytes[15] = (byte)n;
        return new Guid(bytes, bigEndian: true);
    }

    private async Task Import(IEnumerable<GenericRecord> records)
    {
        Directory.CreateDirectory(root);
        await using var store = new Store(Database);
        await store.ImportAsync(records.Select(r => new RecordImport(r, new Dictionary<int, int>())), Guid.NewGuid());
    }

    private object? Scalar(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    [Fact]
    public async Task EveryRecordInOneChunkIsStillCorrectJustSlower()
    {
        var ids = Enumerable.Range(0, 300).Select(n => InChunk(0xABC, n)).ToArray();
        Assert.All(ids, id => Assert.Equal(0xABC, Store.ChunkOf(id)));
        await Import(ids.Select((id, n) => Generic(id, n)));
        Assert.Equal(1L, Scalar("SELECT count(*) FROM RecordChunks"));
        Assert.Equal(300L, Scalar("SELECT Count FROM RecordChunks WHERE ChunkId = 2748"));
        await using var store = new Store(Database);
        await store.VerifyAsync();
        var chunk = await store.ReadChunkAsync(0xABC);
        Assert.Equal(300, chunk.Count);
        Assert.Equal(ids.Order().ToArray(), chunk.Select(r => r.Id).Order().ToArray());
        Assert.Empty(await store.ReadChunkAsync(0xABD));
        Assert.Equal("value 7", (await store.ReadRecordAsync(ids[7]))!.Values["text"].GetString());
        Assert.Equal(300, (await store.ReadHeadersAsync()).Count);
    }

    [Fact]
    public async Task ChunkBoundariesAreTheFirstTwelveBitsOfTheBigEndianGuid()
    {
        var edges = new[] { 0, 1, 15, 16, 255, 256, 4095 }.Select(c => (Chunk: c, Id: InChunk(c, 1))).ToArray();
        await Import(edges.Select((e, n) => Generic(e.Id, n)));
        await using var store = new Store(Database);
        foreach (var (chunk, id) in edges)
        {
            Assert.Equal(chunk, Store.ChunkOf(id));
            Assert.Equal(id, Assert.Single(await store.ReadChunkAsync(chunk)).Id);
        }
        Assert.Equal(edges.Length, (long)Scalar("SELECT count(*) FROM RecordChunks")!);
    }

    [Fact]
    public async Task ATombstoneKeepsItsHeaderLeavesItsChunkAndIsNeverServed()
    {
        var ids = Enumerable.Range(0, 40).Select(n => InChunk(n % 3 * 100, n)).ToArray();
        await Import(ids.Select((id, n) => Generic(id, n)));
        var victim = InChunk(100, 999);
        await using (var store = new Store(Database))
        {
            var tombstone = Generic(victim, 999);
            tombstone.DeletedAt = DateTimeOffset.UtcNow;
            await store.CommitChangesAsync([new CreateImportedRecord(tombstone)], Guid.NewGuid());
            Assert.Null(await store.ReadRecordAsync(victim));
            await store.VerifyAsync();
        }
        Assert.Equal(1L, Scalar("SELECT Deleted FROM RecordHeaders WHERE hex(Id) = '" + victim.ToString("N").ToUpperInvariant() + "'"));
        Assert.Equal(40L, Scalar("SELECT sum(Count) FROM RecordChunks"));
        await using var reader = Store.OpenPublishedReader(Database);
        Assert.Equal(41, (await reader.ReadHeadersAsync()).Count);
        Assert.DoesNotContain(await reader.ReadChunkAsync(Store.ChunkOf(victim)), r => r.Id == victim);
    }

    [Fact]
    public async Task TheTombstoneReadPublicationsFallBackOnFindsEveryLatestDeletedRecord()
    {
        var ids = Enumerable.Range(0, 20).Select(n => InChunk(n % 3 * 100, n)).ToArray();
        await Import(ids.Select((id, n) => Generic(id, n)));
        var victims = new[] { InChunk(100, 200), InChunk(2000, 201) };
        await using var store = new Store(Database);
        Assert.Empty(await store.ReadTombstonesAsync());
        foreach (var victim in victims)
        {
            var tombstone = Generic(victim, 200);
            tombstone.DeletedAt = DateTimeOffset.UtcNow;
            await store.CommitChangesAsync([new CreateImportedRecord(tombstone)], Guid.NewGuid());
        }
        var found = await store.ReadTombstonesAsync();
        Assert.Equal(victims.OrderBy(v => v), found.Select(h => h.Id).OrderBy(v => v));
        Assert.All(found, h => Assert.True(h.Deleted));
    }

    [Fact]
    public async Task ARandomStoreHasManyChunksWhoseRowsSumToTheStateDigest()
    {
        await Import(Enumerable.Range(0, 2000).Select(n => Generic(Guid.NewGuid(), n)));
        Assert.InRange((long)Scalar("SELECT count(*) FROM RecordChunks")!, 1000, 2000);
        await using var store = new Store(Database);
        await store.VerifyAsync();
        var total = 0;
        for (var chunk = 0; chunk < Store.ChunkCount; chunk++) total += (await store.ReadChunkAsync(chunk)).Count;
        Assert.Equal(2000, total);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

/// <summary>The private Indonesian copy: chunk sums, the ranged read and verification on a real project.</summary>
public sealed class DerivedTablesIndonesianTests(ImportedProject project) : IClassFixture<ImportedProject>
{
    [PrivateProjectFact("MOTIF_RECORD_STORE_INDONESIAN")]
    public async Task ChunksSumHeadersMatchAndEveryRangedReadEqualsTheFullRead()
    {
        var path = project.CopyStore();
        await using var store = new Store(path);
        await store.VerifyAsync();
        var full = await store.ReadAsync();
        var seen = 0;
        for (var chunk = 0; chunk < Store.ChunkCount; chunk++)
        {
            var expected = full.Where(r => Store.ChunkOf(r.Id) == chunk).ToArray();
            var ranged = await store.ReadChunkAsync(chunk);
            Assert.Equal(expected.Select(Store.SnapshotItem), ranged.Select(Store.SnapshotItem));
            Assert.Equal(expected.Select(r => r.Id), (await store.ReadChunkUnverifiedAsync(chunk)).Select(r => r.Id));
            seen += expected.Length;
        }
        Assert.Equal(full.Count, seen);
        Assert.Equal(full.Count, (await store.ReadHeadersAsync()).Count);
    }
}
