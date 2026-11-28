using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace SIL.Motif.RecordStore;

/// <summary>One row of the headers table: what a reference or ownership query needs without decoding the record.</summary>
public readonly record struct RecordHeader(Guid Id, string Kind, int ClassId, Guid? Owner, int OwningField, bool Deleted)
{
    internal static RecordHeader From(GenericRecord record) =>
        new(record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField, record.DeletedAt is not null);

    /// <summary>The bytes the headers digest hashes; the kind is last so no field can borrow another's bytes.</summary>
    internal byte[] Item()
    {
        var kind = Encoding.UTF8.GetBytes(Kind);
        var bytes = new byte[16 + 1 + 4 + 4 + 1 + 16 + kind.Length];
        Id.TryWriteBytes(bytes.AsSpan(0, 16), bigEndian: true, out _);
        bytes[16] = Deleted ? (byte)1 : (byte)0;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(17, 4), ClassId);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(21, 4), OwningField);
        bytes[25] = Owner is null ? (byte)0 : (byte)1;
        Owner?.TryWriteBytes(bytes.AsSpan(26, 16), bigEndian: true, out _);
        kind.CopyTo(bytes, 42);
        return bytes;
    }
}

/// <summary>The derived tables computed from a full decode: the 4,096 chunk accumulators and every record's header.</summary>
internal sealed record DerivedSnapshot(SetDigest[] Chunks, RecordHeader[] Headers)
{
    public SetDigest Total() => RecordStore.Total(Chunks);
    public SetDigest HeadersDigest() => RecordStore.HeadersDigest(Headers);
}

public sealed partial class RecordStore
{
    /// <summary>A chunk is named by the first 12 bits of the record's big-endian GUID.</summary>
    internal const int ChunkCount = 4096;

    internal static int ChunkOf(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes, bigEndian: true, out _);
        return (bytes[0] << 4) | (bytes[1] >> 4);
    }

    private static (byte[] Low, byte[] High) ChunkRange(int chunk)
    {
        var low = new byte[16];
        var high = new byte[16];
        Array.Fill(high, (byte)0xFF);
        low[0] = high[0] = (byte)(chunk >> 4);
        low[1] = (byte)((chunk & 15) << 4);
        high[1] = (byte)(((chunk & 15) << 4) | 0x0F);
        return (low, high);
    }

    internal static SetDigest Total(IEnumerable<SetDigest> chunks)
    {
        var total = new SetDigest();
        foreach (var chunk in chunks) total = total.Combine(chunk);
        return total;
    }

    internal static SetDigest HeadersDigest(IEnumerable<RecordHeader> headers)
    {
        var digest = new SetDigest();
        foreach (var header in headers) digest = digest.Add(header.Item());
        return digest;
    }

    internal static SetDigest[] ChunkDigests(IEnumerable<GenericRecord> records, CancellationToken ct = default)
    {
        var chunks = new SetDigest[ChunkCount];
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = ChunkOf(record.Id);
            chunks[chunk] = chunks[chunk].Add(SnapshotItem(record));
        }
        return chunks;
    }

    private const string HeadersTable = """
        CREATE TABLE IF NOT EXISTS RecordHeaders (Id BLOB NOT NULL PRIMARY KEY, Kind TEXT NOT NULL, ClassId INTEGER NOT NULL,
            Owner BLOB NULL, OwningField INTEGER NOT NULL, Deleted INTEGER NOT NULL) WITHOUT ROWID
        """;

    private const string ChunksTable = """
        CREATE TABLE IF NOT EXISTS RecordChunks (ChunkId INTEGER NOT NULL PRIMARY KEY, Count INTEGER NOT NULL, Digest TEXT NOT NULL)
        """;

    private const string BindingTable = """
        CREATE TABLE IF NOT EXISTS RecordPublicationBinding (Id INTEGER NOT NULL PRIMARY KEY, Revision TEXT NOT NULL,
            SnapshotDigest TEXT NOT NULL, ReplayDigest TEXT NOT NULL, ContentDigest TEXT NOT NULL, IndexDigest TEXT NOT NULL,
            HeadersDigest TEXT NOT NULL)
        """;

    private static void CreateDerivedTables(RecordDatabase database)
    {
        database.Database.ExecuteSqlRaw(HeadersTable);
        database.Database.ExecuteSqlRaw(ChunksTable);
        database.Database.ExecuteSqlRaw(BindingTable);
    }

    private SqliteCommand DerivedCommand(string sql)
    {
        var command = ((SqliteConnection)database.Database.GetDbConnection()).CreateCommand();
        command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction;
        command.CommandText = sql;
        return command;
    }

    /// <summary>True when the binding row copies the state row; any other binding is stale.</summary>
    private bool BindingMatches(StoreState state)
    {
        try
        {
            return database.Database.SqlQuery<int>($"""
                SELECT count(*) AS Value FROM RecordPublicationBinding WHERE Id = 1 AND Revision = {state.Revision}
                AND SnapshotDigest = {state.SnapshotDigest} AND ReplayDigest = {state.ReplayDigest}
                AND ContentDigest = {state.ContentDigest} AND IndexDigest = {state.IndexDigest}
                """).AsEnumerable().Single() == 1;
        }
        catch (SqliteException) { return false; }
    }

    private async Task<(string Revision, string Snapshot, string Replay, string Content, string Index, string Headers)?> ReadBindingAsync(CancellationToken ct)
    {
        await using var command = DerivedCommand("SELECT Revision, SnapshotDigest, ReplayDigest, ContentDigest, IndexDigest, HeadersDigest FROM RecordPublicationBinding WHERE Id = 1");
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
    }

    /// <summary>The stored binding must copy the state row; otherwise no derived table may be consumed.</summary>
    private async Task RequireBindingAsync(StoreState state, CancellationToken ct)
    {
        var binding = await ReadBindingAsync(ct);
        if (binding is null) throw new InvalidDataException("RecordPublicationBinding: the binding row is missing.");
        var (revision, snapshot, replay, contentDigest, index, _) = binding.Value;
        if (revision != state.Revision || snapshot != state.SnapshotDigest || replay != state.ReplayDigest ||
            contentDigest != state.ContentDigest || index != state.IndexDigest)
            throw new InvalidDataException("RecordPublicationBinding: the binding row does not match RecordStoreState; run RebuildReferenceIndexAsync (store-rebuild-index) to rewrite the derived tables.");
    }

    /// <summary>A chunk-moving write rebinds only a binding that matched the point it began from.</summary>
    private Task RequireBindingBeforeWriteAsync(StoreState before) => RequireBindingAsync(before, default);

    /// <summary>Tombstone scans this handle ran; a publication that writes no derived tables runs none.</summary>
    internal int TombstoneReads { get; private set; }

    /// <summary>The latest deleted snapshots as headers, for publications that decoded only live records.</summary>
    internal async Task<RecordHeader[]> ReadTombstonesAsync()
    {
        TombstoneReads++;
        var blobs = new List<byte[]>();
        await database.Database.OpenConnectionAsync();
        try
        {
            // The grouped scan reads ids and the deleted flag; only a deleted record's body is fetched.
            var ids = new List<byte[]>();
            await using (var command = DerivedCommand(TombstoneIdSql))
            {
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) ids.Add((byte[])reader.GetValue(0));
            }
            foreach (var id in ids)
            {
                await using var command = DerivedCommand("SELECT Entity FROM Snapshots WHERE Id = $id");
                command.Parameters.AddWithValue("$id", id);
                if (await command.ExecuteScalarAsync() is byte[] blob) blobs.Add(blob);
            }
        }
        finally { await database.Database.CloseConnectionAsync(); }
        return blobs.Select(blob => (GenericRecord)database.Codec.Snapshot(blob)).Where(r => r.DeletedAt is not null)
            .Select(RecordHeader.From).ToArray();
    }

    private const string TombstoneIdSql = """
        SELECT SnapshotId FROM (
            SELECT s.Id AS SnapshotId, s.EntityIsDeleted AS Deleted, max(c.DateTime || '!' || printf('%020d', c.Counter) || '!' || c.Id)
            FROM Snapshots AS s INNER JOIN Commits AS c ON s.CommitId = c.Id GROUP BY s.EntityId) WHERE Deleted = 1
        """;

    /// <summary>Replaces the chunk, header and binding rows from a full decode, in the open transaction.</summary>
    private void WriteDerived(StoreState state, SetDigest[] chunks, RecordHeader[] headers)
    {
        using (var command = DerivedCommand("DELETE FROM RecordChunks")) command.ExecuteNonQuery();
        using (var insert = DerivedCommand("INSERT INTO RecordChunks (ChunkId, Count, Digest) VALUES ($chunk, $count, $digest)"))
        {
            var chunk = insert.Parameters.Add("$chunk", SqliteType.Integer);
            var count = insert.Parameters.Add("$count", SqliteType.Integer);
            var digest = insert.Parameters.Add("$digest", SqliteType.Text);
            for (var i = 0; i < chunks.Length; i++)
            {
                if (chunks[i].Count == 0) continue;
                chunk.Value = i;
                count.Value = chunks[i].Count;
                digest.Value = chunks[i].ToText();
                insert.ExecuteNonQuery();
            }
        }
        WriteHeaders(headers);
        UpsertBinding(state, HeadersDigest(headers));
    }

    private void WriteHeaders(RecordHeader[] headers)
    {
        using (var command = DerivedCommand("DELETE FROM RecordHeaders")) command.ExecuteNonQuery();
        var keys = headers.Select(h => IdKey(h.Id)).ToArray();
        var ordered = headers.ToArray();
        Array.Sort(keys, ordered);
        using var insert = DerivedCommand("INSERT INTO RecordHeaders (Id, Kind, ClassId, Owner, OwningField, Deleted) VALUES ($id, $kind, $class, $owner, $field, $deleted)");
        var id = insert.Parameters.Add("$id", SqliteType.Blob);
        var kind = insert.Parameters.Add("$kind", SqliteType.Text);
        var classId = insert.Parameters.Add("$class", SqliteType.Integer);
        var owner = insert.Parameters.Add("$owner", SqliteType.Blob);
        var field = insert.Parameters.Add("$field", SqliteType.Integer);
        var deleted = insert.Parameters.Add("$deleted", SqliteType.Integer);
        insert.Prepare();
        foreach (var header in ordered)
        {
            id.Value = NetworkGuidConverter.Write(header.Id);
            kind.Value = header.Kind;
            classId.Value = header.ClassId;
            owner.Value = header.Owner is { } o ? NetworkGuidConverter.Write(o) : DBNull.Value;
            field.Value = header.OwningField;
            deleted.Value = header.Deleted ? 1 : 0;
            insert.ExecuteNonQuery();
        }
    }

    private static UInt128 IdKey(Guid value)
    {
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes, bigEndian: true, out _);
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt128BigEndian(bytes);
    }

    private void UpsertBinding(StoreState state, SetDigest headersDigest)
    {
        using var command = DerivedCommand("""
            INSERT INTO RecordPublicationBinding (Id, Revision, SnapshotDigest, ReplayDigest, ContentDigest, IndexDigest, HeadersDigest)
            VALUES (1, $revision, $snapshot, $replay, $content, $index, $headers)
            ON CONFLICT (Id) DO UPDATE SET Revision = $revision, SnapshotDigest = $snapshot, ReplayDigest = $replay,
                ContentDigest = $content, IndexDigest = $index, HeadersDigest = $headers
            """);
        command.Parameters.AddWithValue("$revision", state.Revision);
        command.Parameters.AddWithValue("$snapshot", state.SnapshotDigest);
        command.Parameters.AddWithValue("$replay", state.ReplayDigest);
        command.Parameters.AddWithValue("$content", state.ContentDigest);
        command.Parameters.AddWithValue("$index", state.IndexDigest);
        command.Parameters.AddWithValue("$headers", headersDigest.ToText());
        command.ExecuteNonQuery();
    }

    /// <summary>A scalar write moves one chunk by the old and new item and rebinds; headers do not change.</summary>
    private void MoveChunk(StoreState state, int chunk, byte[] oldItem, byte[] newItem)
    {
        SetDigest current;
        using (var read = DerivedCommand("SELECT Digest FROM RecordChunks WHERE ChunkId = $chunk"))
        {
            read.Parameters.AddWithValue("$chunk", chunk);
            if (read.ExecuteScalar() is not string text || !SetDigest.TryParse(text, out current))
                throw new InvalidDataException($"RecordChunks: chunk {chunk} is missing or malformed; run RebuildReferenceIndexAsync (store-rebuild-index) to rewrite the derived tables.");
        }
        var next = current.Remove(oldItem).Add(newItem);
        using (var write = DerivedCommand("UPDATE RecordChunks SET Count = $count, Digest = $digest WHERE ChunkId = $chunk"))
        {
            write.Parameters.AddWithValue("$chunk", chunk);
            write.Parameters.AddWithValue("$count", next.Count);
            write.Parameters.AddWithValue("$digest", next.ToText());
            write.ExecuteNonQuery();
        }
        using var bind = DerivedCommand("""
            UPDATE RecordPublicationBinding SET Revision = $revision, SnapshotDigest = $snapshot, ReplayDigest = $replay,
                ContentDigest = $content, IndexDigest = $index WHERE Id = 1
            """);
        bind.Parameters.AddWithValue("$revision", state.Revision);
        bind.Parameters.AddWithValue("$snapshot", state.SnapshotDigest);
        bind.Parameters.AddWithValue("$replay", state.ReplayDigest);
        bind.Parameters.AddWithValue("$content", state.ContentDigest);
        bind.Parameters.AddWithValue("$index", state.IndexDigest);
        if (bind.ExecuteNonQuery() != 1) throw new InvalidDataException("RecordPublicationBinding: the binding row is missing.");
    }

    private async Task<Dictionary<int, SetDigest>> ReadChunkRowsAsync(CancellationToken ct)
    {
        var rows = new Dictionary<int, SetDigest>();
        await using var command = DerivedCommand("SELECT ChunkId, Count, Digest FROM RecordChunks");
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt32(0);
            if (!SetDigest.TryParse(reader.GetString(2), out var digest) || digest.Count != reader.GetInt64(1) || id is < 0 or >= ChunkCount)
                throw new InvalidDataException($"RecordChunks: chunk {id} is malformed.");
            rows[id] = digest;
        }
        return rows;
    }

    private async Task<List<RecordHeader>> ReadHeaderRowsAsync(byte[]? low, byte[]? high, CancellationToken ct)
    {
        var rows = new List<RecordHeader>();
        await using var command = DerivedCommand(low is null
            ? "SELECT Id, Kind, ClassId, Owner, OwningField, Deleted FROM RecordHeaders ORDER BY Id"
            : "SELECT Id, Kind, ClassId, Owner, OwningField, Deleted FROM RecordHeaders WHERE Id BETWEEN $low AND $high ORDER BY Id");
        if (low is not null)
        {
            command.Parameters.AddWithValue("$low", low);
            command.Parameters.AddWithValue("$high", high!);
        }
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is not byte[] { Length: 16 } id || reader.IsDBNull(3) is false && reader.GetValue(3) is not byte[] { Length: 16 })
                throw new InvalidDataException("RecordHeaders: a header row is malformed.");
            rows.Add(new(new Guid(id, bigEndian: true), reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : new Guid((byte[])reader.GetValue(3), bigEndian: true), reader.GetInt32(4), reader.GetInt32(5) != 0));
        }
        return rows;
    }

    /// <summary>The headers table, read in id order and checked against the binding's headers digest.</summary>
    public Task<IReadOnlyList<RecordHeader>> ReadHeadersAsync(CancellationToken ct = default) => ReadPublishedAsync<IReadOnlyList<RecordHeader>>(async state =>
    {
        await RequireDerivedTrustedAsync(state, ct);
        var rows = await ReadHeaderRowsAsync(null, null, ct);
        var binding = await ReadBindingAsync(ct);
        if (HeadersDigest(rows).ToText() != binding!.Value.Headers)
            throw new InvalidDataException("RecordHeaders: the header rows do not match the headers digest in RecordPublicationBinding.");
        return rows;
    }, ct);

    /// <summary>
    /// The live records of one chunk (copies), read by a range scan and checked before they are returned: their items must sum to the
    /// chunk's stored digest and the chunk's header rows must equal their headers. Nothing outside the chunk is read or checked.
    /// </summary>
    public Task<IReadOnlyList<GenericRecord>> ReadChunkAsync(int chunk, CancellationToken ct = default) =>
        ReadPublishedAsync<IReadOnlyList<GenericRecord>>(async state =>
            (await VerifiedChunkAsync(chunk, state, ct)).Records.Select(r => (GenericRecord)r.Copy()).ToList(), ct);

    /// <summary>One record by id, from its checked chunk (a copy); null when no live record has the id.</summary>
    public Task<GenericRecord?> ReadRecordAsync(Guid id, CancellationToken ct = default) =>
        ReadPublishedAsync(async state => (GenericRecord?)(await VerifiedChunkAsync(ChunkOf(id), state, ct)).ById.GetValueOrDefault(id)?.Copy(), ct);

    private sealed record VerifiedChunk(IReadOnlyList<GenericRecord> Records, Dictionary<Guid, GenericRecord> ById);

    private readonly object chunkCacheLock = new();
    private StoreState? chunkCacheState;
    private readonly Dictionary<int, VerifiedChunk> chunkCache = [];

    private async Task<VerifiedChunk> VerifiedChunkAsync(int chunk, StoreState state, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunk);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunk, ChunkCount);
        await RequireDerivedTrustedAsync(state, ct);
        lock (chunkCacheLock)
        {
            if (chunkCacheState is null || !chunkCacheState.SamePoint(state)) { chunkCache.Clear(); chunkCacheState = state; }
            if (chunkCache.TryGetValue(chunk, out var cached)) return cached;
        }
        var (low, high) = ChunkRange(chunk);
        var decoded = await DecodeRangeAsync(low, high, ct);
        var digest = new SetDigest();
        var live = new List<GenericRecord>();
        foreach (var record in decoded)
        {
            if (record.DeletedAt is not null) continue;
            live.Add(record);
            digest = digest.Add(SnapshotItem(record));
        }
        SetDigest stored;
        await using (var command = DerivedCommand("SELECT Digest FROM RecordChunks WHERE ChunkId = $chunk"))
        {
            command.Parameters.AddWithValue("$chunk", chunk);
            var text = await command.ExecuteScalarAsync(ct);
            if (text is null) stored = new SetDigest();
            else if (text is not string s || !SetDigest.TryParse(s, out stored))
                throw new InvalidDataException($"RecordChunks: chunk {chunk} is malformed.");
        }
        if (stored != digest)
            throw new InvalidDataException($"RecordChunks: chunk {chunk} does not match its stored snapshots (Snapshots or RecordChunks is damaged).");
        var headers = await ReadHeaderRowsAsync(low, high, ct);
        var expected = decoded.Select(RecordHeader.From).OrderBy(h => IdKey(h.Id)).ToList();
        if (!headers.SequenceEqual(expected))
            throw new InvalidDataException($"RecordHeaders: the header rows of chunk {chunk} do not match the stored snapshots.");
        SnapshotsDecoded += decoded.Count;
        var verified = new VerifiedChunk(live, live.ToDictionary(r => r.Id));
        lock (chunkCacheLock)
        {
            if (chunkCacheState is not null && chunkCacheState.SamePoint(state)) chunkCache[chunk] = verified;
        }
        return verified;
    }

    /// <summary>The latest snapshot of every entity whose id lies in the range, live or deleted, in Harmony's order.</summary>
    internal async Task<IReadOnlyList<GenericRecord>> DecodeRangeAsync(byte[] low, byte[] high, CancellationToken ct)
    {
        var blobs = new List<byte[]>();
        await using (var command = DerivedCommand(LatestRangeSql))
        {
            command.Parameters.AddWithValue("$low", low);
            command.Parameters.AddWithValue("$high", high);
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) blobs.Add((byte[])reader.GetValue(0));
        }
        var records = new List<GenericRecord>(blobs.Count);
        foreach (var blob in blobs)
        {
            ct.ThrowIfCancellationRequested();
            records.Add((GenericRecord)database.Codec.Snapshot(blob));
        }
        return records;
    }

    /// <summary>Test seam: the ranged latest-snapshot read of one chunk, unverified, so it can be compared with the full read.</summary>
    internal Task<IReadOnlyList<GenericRecord>> ReadChunkUnverifiedAsync(int chunk, CancellationToken ct = default) =>
        ReadPublishedAsync(async _ =>
        {
            var (low, high) = ChunkRange(chunk);
            return (IReadOnlyList<GenericRecord>)(await DecodeRangeAsync(low, high, ct)).Where(r => r.DeletedAt is null).ToList();
        }, ct);

    // The latest snapshot per entity, picked as Harmony's own query does (bare columns of the max row).
    private const string LatestRangeSql = """
        SELECT s.Entity, max(c.DateTime || '!' || printf('%020d', c.Counter) || '!' || c.Id)
        FROM Snapshots AS s INNER JOIN Commits AS c ON s.CommitId = c.Id
        WHERE s.EntityId >= $low AND s.EntityId <= $high
        GROUP BY s.EntityId ORDER BY s.EntityId
        """;

    /// <summary>Compares the derived tables with a full decode; each difference names its table.</summary>
    private async Task AddDerivedFindingsAsync(List<string> findings, StoreState state, DerivedSnapshot derived, CancellationToken ct)
    {
        try
        {
            var binding = await ReadBindingAsync(ct);
            var headersDigest = derived.HeadersDigest().ToText();
            if (binding is null) findings.Add("RecordPublicationBinding: the binding row is missing");
            else
            {
                var (revision, snapshot, replay, contentDigest, index, headers) = binding.Value;
                if (revision != state.Revision || snapshot != state.SnapshotDigest || replay != state.ReplayDigest ||
                    contentDigest != state.ContentDigest || index != state.IndexDigest)
                    findings.Add("RecordPublicationBinding: it does not copy RecordStoreState");
                if (headers != headersDigest) findings.Add("RecordPublicationBinding: its headers digest differs from the decoded records");
            }
            var rows = await ReadChunkRowsAsync(ct);
            if (Total(rows.Values).Final("snapshot") != state.SnapshotDigest)
                findings.Add("RecordChunks: the chunk digests do not sum to the stored snapshot digest");
            var differing = Enumerable.Range(0, ChunkCount)
                .Where(chunk => rows.GetValueOrDefault(chunk) != derived.Chunks[chunk]).ToArray();
            if (differing.Length > 0)
                findings.Add($"RecordChunks: {differing.Length} chunk rows differ from the decoded records (first: chunk {differing[0]})");
            var held = await ReadHeaderRowsAsync(null, null, ct);
            var wanted = derived.Headers.OrderBy(h => IdKey(h.Id)).ToList();
            if (!held.SequenceEqual(wanted))
                findings.Add($"RecordHeaders: it holds {held.Count} rows that do not match the {wanted.Count} decoded records");
        }
        catch (SqliteException failure) { findings.Add("Derived tables: " + failure.Message); }
        catch (InvalidDataException failure) { findings.Add(failure.Message); }
    }
}
