using SIL.Harmony.Changes;
using SIL.Motif.RecordStore.HandAnalysis;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

public sealed record RecordImport(GenericRecord Record, IReadOnlyDictionary<int, int> FieldTypes);
public sealed record RecordCommit(Guid Id, DateTimeOffset UtcTime, long Counter, int ChangeCount);

public sealed partial class RecordStore : IAsyncDisposable
{
    public const int MaximumChangesPerCommit = 500;
    private const int StoreFormat = 5;
    private const int LegacyStoreFormat = 4;
    internal const int CompleteRecordModelVersion = 1;
    private readonly int format;
    private int ModelVersion => format == LegacyStoreFormat ? 0 : CompleteRecordModelVersion;
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly DataModel model;
    internal DataModel Model => model;
    private readonly RecordDatabase database;
    private readonly RecordContent content;
    private readonly bool readOnly;
    public static SIL.ListCrdt.ListLimits ListLimits => StoreListLimits.Default;
    public IRecordList Lists { get; } = new CrdtRecordList();

    public RecordStore(string path, TimeProvider? timeProvider = null) : this(ResolvePath(path), timeProvider, false) { }

    internal static RecordStore OpenPublishedReader(string resolvedPath) => new(resolvedPath, null, true);

    private RecordStore(string path, TimeProvider? timeProvider, bool readOnly)
    {
        this.readOnly = readOnly;
        writeLockPath = path + ".write.lock";
        using var writer = readOnly ? null : AcquireWriter();
        var services = new ServiceCollection();
        var connection = RecordSqlite.ConnectionString(path, readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate);
        var storedFormat = File.Exists(path) ? ReadFormat(connection) : 0;
        if (storedFormat is not (0 or LegacyStoreFormat or StoreFormat))
            throw new InvalidDataException("Unsupported record store format.");
        format = storedFormat == LegacyStoreFormat ? LegacyStoreFormat : StoreFormat;
        services.AddSingleton<RecordContent>();
        // Converters close over this store's immutable content cache; their EF model must have the same lifetime.
        services.AddDbContext<RecordDatabase>(options => options.UseSqlite(connection).EnableServiceProviderCaching(false));
        services.AddCrdtData<RecordDatabase>(config =>
        {
            config.EnableProjectedTables = false;
            config.ObjectTypeListBuilder.DefaultAdapter().Add<GenericRecord>();
            config.ChangeTypeListBuilder.Add<CreateRecord>().Add<SetRecordValue>()
                .Add<SetRecordAlternative>().Add<InsertRecordItem>().Add<ListRecordOp>();
            config.ModelVersionBuilder.Add(0, "Motif generic records", version => version
                .Change<CreateRecord>().Change<SetRecordValue>().Change<SetRecordAlternative>()
                .Change<InsertRecordItem>().Change<ListRecordOp>());
            if (ModelVersion >= CompleteRecordModelVersion)
            {
                config.ChangeTypeListBuilder.Add<CreateImportedRecord>();
                config.ModelVersionBuilder.Add(CompleteRecordModelVersion, "Motif complete-record import",
                    version => version.Change<CreateImportedRecord>());
            }
        });
        services.AddSingleton<TimeProvider>(new MicrosecondTimeProvider(timeProvider ?? TimeProvider.System));
        provider = services.BuildServiceProvider();
        scope = provider.CreateAsyncScope();
        try
        {
            database = scope.ServiceProvider.GetRequiredService<RecordDatabase>();
            if (readOnly)
                database.Database.GetDbConnection().StateChange += PublishedHomeConnection.ConfigureReader;
            else
                database.Database.GetDbConnection().StateChange += PublishedHomeConnection.ConfigureWriter;
            var created = !readOnly && database.Database.EnsureCreated();
            if (created)
            {
                using var command = database.Database.GetDbConnection().CreateCommand();
                database.Database.OpenConnection();
                command.CommandText = $"PRAGMA user_version={format}";
                command.ExecuteNonQuery();
                database.Database.CloseConnection();
            }
            else if (storedFormat == 0 && File.Exists(path))
                throw new InvalidDataException("Unsupported record store format.");
            // A bounded index probe, not a scan of every change body, finds history authored by a newer model.
            database.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS IX_ChangeEntities_AuthoredModelVersion ON ChangeEntities (AuthoredModelVersion)");
            var unsupported = database.Database.SqlQuery<int>($"""
                SELECT AuthoredModelVersion AS Value FROM ChangeEntities WHERE AuthoredModelVersion < 0
                UNION ALL SELECT AuthoredModelVersion AS Value FROM ChangeEntities WHERE AuthoredModelVersion > {ModelVersion} LIMIT 1
                """).AsEnumerable().Select(value => (int?)value).FirstOrDefault();
            if (unsupported is int version) RequireSupportedModelVersion(version, ModelVersion);
            content = scope.ServiceProvider.GetRequiredService<RecordContent>();
            if (!readOnly) content.Load(database.Set<ContentRow>().AsNoTracking().ToArray());
            if (created)
            {
                ReferencePersistence.Create(database);
                database.Add(new StoreState());
                database.SaveChanges();
            }
            var state = database.Set<StoreState>().AsNoTracking().Single();
            if (readOnly && !state.Published) throw new InvalidDataException("The store import has not been published.");
            if (!readOnly && state.Published && state.ContentDigest != content.Digest())
                throw new InvalidDataException("Corrupt immutable record content.");
            database.ChangeTracker.Clear();
            model = scope.ServiceProvider.GetRequiredService<DataModel>();
        }
        catch
        {
            scope.Dispose();
            provider.Dispose();
            throw;
        }
    }

    internal static bool IsPublishedPoint(string path, StoreState point)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(path, SqliteOpenMode.ReadOnly));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(SELECT 1 FROM "RecordStoreState" WHERE "Published" = 1 AND "Revision" = $revision
                AND "SnapshotDigest" = $snapshot AND "ReplayDigest" = $replay AND "ContentDigest" = $content
                AND "IndexDigest" = $index AND "IndexLimitations" = $limitations)
            """;
        command.Parameters.AddWithValue("$revision", point.Revision);
        command.Parameters.AddWithValue("$snapshot", point.SnapshotDigest);
        command.Parameters.AddWithValue("$replay", point.ReplayDigest);
        command.Parameters.AddWithValue("$content", point.ContentDigest);
        command.Parameters.AddWithValue("$index", point.IndexDigest);
        command.Parameters.AddWithValue("$limitations", point.IndexLimitations);
        return (long)command.ExecuteScalar()! == 1;
    }

    private static int ReadFormat(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void RequireSupportedModelVersion(int authored, int supported)
    {
        if (authored < 0 || authored > supported)
            throw new InvalidDataException($"Unsupported Harmony model version {authored}. This reader supports through version {supported}.");
    }

    public static Guid AuxiliaryId(string kind, string identity)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store/" + kind + "/" + identity)));
        return Guid.ParseExact(hex[..32], "N");
    }

    public Task ImportAsync(IEnumerable<RecordImport> records, Guid clientId, NativeImportCoverage? nativeCoverage = null) =>
        ImportAsync(records, clientId, nativeCoverage, packed: false);

    internal Task ImportNativeAsync(IEnumerable<RecordImport> records, Guid clientId, NativeImportCoverage coverage) =>
        ImportAsync(records, clientId, coverage, packed: format == StoreFormat);

    private async Task ImportAsync(IEnumerable<RecordImport> records, Guid clientId, NativeImportCoverage? nativeCoverage, bool packed)
    {
        using var writer = AcquireWriter();
        await RequireEmptyImportAsync();
        var imported = records.ToList();
        if (nativeCoverage is not null)
        {
            var basis = new GenericRecord { Id = AuxiliaryId("native-coverage", "project"), Kind = "native-coverage" };
            basis.Values["basis"] = JsonSerializer.SerializeToElement(nativeCoverage);
            imported.Add(new RecordImport(basis, new Dictionary<int, int>()));
        }
        var materialized = imported.ToArray();
        if (materialized.Select(r => r.Record.Id).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("Duplicate record identity in import.");
        // Layouts are validated for every group before the first change is written, so a refusal leaves no history.
        var layouts = materialized.GroupBy(r => (r.Record.Kind, r.Record.ClassId, Fields: packed && r.Record.Kind == "lcm"
                ? "" : JsonSerializer.Serialize(r.Record.Values.Keys.Order(StringComparer.Ordinal))))
            .Select(group => (Group: group, Layout: RecordInitialization.Layout(group, requireDefaults: packed))).ToList();
        var prepared = layouts.SelectMany(entry => entry.Group.Select(r =>
            new RecordImport(RecordInitialization.Prepare(r.Record, entry.Layout, reuseValues: packed), r.FieldTypes)));
        var baseline = packed ? prepared.Select(import => import.Record).ToArray() : null;
        await using var transaction = await database.Database.BeginTransactionAsync();
        PublishedWork? work = null;
        try
        {
            if (baseline is not null)
            {
                work = PublishedWork.Start(baseline);
                var clock = scope.ServiceProvider.GetRequiredService<IHybridDateTimeProvider>();
                await new NativeImportWriter(database, content).WriteAsync(baseline, clientId, clock);
            }
            else
            {
                // Harmony's AddManyChanges starts its own transaction; AddChanges joins this one.
                foreach (var batch in Changes(prepared).Chunk(MaximumChangesPerCommit))
                    await model.AddChanges(clientId, batch);
                database.ChangeTracker.Clear();
            }
            await PublishIndexAsync(baseline ?? await ReadRecordsAsync(), await CommitRevisionAsync(), work);
            await transaction.CommitAsync();
        }
        catch
        {
            requiresReopen = true;
            // The overlapped work only reads the records; let it finish so nothing outlives a failed import.
            if (work is not null) await Task.WhenAll(work.Snapshot, work.Projection).ContinueWith(_ => { });
            throw;
        }
    }

    private async Task RequireEmptyImportAsync()
    {
        if (requiresReopen) throw new InvalidOperationException("A failed write requires reopening this handle.");
        point = null;
        if ((await database.Set<StoreState>().AsNoTracking().SingleAsync()).Published || await database.Set<Commit>().AnyAsync())
            throw new InvalidOperationException("Import requires an empty store.");
    }

    internal static IEnumerable<IChange> Changes(IEnumerable<RecordImport> imports)
    {
        foreach (var import in imports)
        {
            var record = import.Record;
            yield return new CreateRecord(record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField,
                record.OwnOrdinal, record.IdentityFields, record.Layout, record.Provenance);
            foreach (var (key, value) in record.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                if (RecordInitialization.IsInitial(record, key, value)) continue;
                var type = int.TryParse(key, out var field) ? import.FieldTypes.GetValueOrDefault(field) : 0;
                if (type is 14 or 16)
                {
                    foreach (var alternative in value.EnumerateObject())
                        yield return new SetRecordAlternative(record.Id, field, alternative.Name, alternative.Value);
                }
                else if (type >= 25)
                {
                    var index = 0;
                    foreach (var item in value.EnumerateArray()) yield return new InsertRecordItem(record.Id, field, item.GetGuid(), index++);
                }
                else yield return new SetRecordValue(record.Id, key, value);
            }
        }
    }

    public Task<IReadOnlyList<GenericRecord>> ReadAsync() => ReadPublishedAsync(async state =>
    {
        var decoded = await DecodeLatestAsync(digest: true, default);
        VerifySnapshots(state, decoded.Digest);
        return decoded.Records;
    });

    private async Task<IReadOnlyList<GenericRecord>> ReadRecordsAsync(CancellationToken ct = default) =>
        (await DecodeLatestAsync(digest: false, ct)).Records;

    /// <summary>Worker threads for decoding, digesting and the history scan; null uses <see cref="ParallelWork.DefaultWorkers"/>.</summary>
    internal int? Workers { get; set; }

    /// <summary>Scans the whole history exactly as a guarded write's first call does; for tests and measurements.</summary>
    internal Task<ReplayState> ScanHistoryAsync(int batchRows = ReplayIntegrity.DefaultBatchRows, CancellationToken ct = default,
        Action<string, long>? batchStarted = null) => ReplayIntegrity.ScanAsync(database, Workers, batchRows, ct, batchStarted);

    /// <summary>Records per decode chunk; small values let tests exercise many chunks on small stores.</summary>
    internal int DecodeChunkSize { get; set; } = 1024;

    /// <summary>Test hook, called with the chunk number as each decode chunk starts (on a worker thread).</summary>
    internal Action<int>? DecodeChunkStarted { get; set; }

    /// <summary>Number of snapshots this handle has decoded for verified reads.</summary>
    internal long SnapshotsDecoded { get; private set; }

    private sealed record DecodedSnapshots(IReadOnlyList<GenericRecord> Records, SetDigest Digest);

    /// <summary>Live records in Harmony's order, decoded on workers, with their digest when asked.</summary>
    private async Task<DecodedSnapshots> DecodeLatestAsync(bool digest, CancellationToken ct)
    {
        // Several failing rows may be reported in another order than sequentially: read errors win.
        var blobs = new List<byte[]>();
        var codec = database.Codec;
        codec.DeferSnapshotDecode = true;
        try
        {
            await foreach (var snapshot in model.GetLatestSnapshots().WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                if (snapshot.Entity is not GenericRecord { Encoded: { } bytes })
                    throw new InvalidOperationException("The latest-snapshot query returned an entity that was not left undecoded.");
                blobs.Add(bytes);
            }
        }
        finally { codec.DeferSnapshotDecode = false; }
        var size = Math.Max(1, DecodeChunkSize);
        var chunks = (blobs.Count + size - 1) / size;
        var decoded = new GenericRecord?[blobs.Count];
        var partial = new SetDigest[chunks];
        var workers = Workers ?? ParallelWork.DefaultWorkers;
        await Task.Run(() => ParallelWork.ForEachChunk(chunks, workers, ct, chunk =>
        {
            DecodeChunkStarted?.Invoke(chunk);
            var sum = new SetDigest();
            var end = Math.Min(blobs.Count, (chunk + 1) * size);
            for (var index = chunk * size; index < end; index++)
            {
                ct.ThrowIfCancellationRequested();
                var record = (GenericRecord)codec.Snapshot(blobs[index]);
                if (record.DeletedAt is not null) continue;
                decoded[index] = record;
                if (digest) sum = sum.Add(SnapshotItem(record));
            }
            partial[chunk] = sum;
        }), ct);
        SnapshotsDecoded += blobs.Count;
        var records = new List<GenericRecord>(blobs.Count);
        foreach (var record in decoded) if (record is not null) records.Add(record);
        var total = new SetDigest();
        foreach (var part in partial) total = total.Combine(part);
        return new(records, total);
    }

    public async Task<IReadOnlyList<RecordCommit>> CommitsAsync() => await database.Set<Commit>().AsNoTracking()
        .Select(c => new RecordCommit(c.Id, c.HybridDateTime.DateTime, c.HybridDateTime.Counter, c.ChangeEntities.Count))
        .ToListAsync();

    public async Task<ReferenceIndexSnapshot> ReferencesAsync(ReferenceEvidence? evidence = null)
    {
        var index = (await PointAsync()).GetPlanningIndex();
        return evidence is null ? index : index.WithObservations(evidence);
    }

    /// <summary>Reads and verifies the published point; a trusted result is retained for reuse.</summary>
    private async Task<VerifiedPoint> AcquireAsync(StoreState state, CancellationToken ct = default)
    {
        point = null;
        var decoded = await DecodeLatestAsync(digest: true, ct);
        var records = decoded.Records;
        var snapshot = VerifySnapshots(state, decoded.Digest);
        var native = new Dictionary<Guid, GenericRecord>();
        var headers = new List<ReferenceRecord>();
        var anchors = new List<GenericRecord>();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            if (record.Kind == "lcm")
            {
                native.Add(record.Id, record);
                headers.Add(new(record.Id, record.ClassId, record.Owner, record.OwningField));
            }
            else if (record.Kind == HandAnalysisKinds.Anchor) anchors.Add(record);
        }
        var projection = new ReferenceProjection(await ReferencePersistence.ReadAsync(database, ct), headers.ToArray(),
            state.IndexLimitations.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var limitations = projection.Limitations.ToList();
        var digest = projection.Accumulate(ct);
        var trusted = digest.Final() == state.IndexDigest;
        if (!trusted) limitations.Add("Incoming-reference index is corrupt; rebuild it before use.");
        var schema = NativeUsageSchema.Read(records, ct);
        var projectClass = schema?.ClassId("LangProject");
        var projects = projectClass.HasValue ? headers.Where(r =>
        {
            ct.ThrowIfCancellationRequested();
            return r.ClassId == projectClass;
        }).Take(2).ToArray() : [];
        var verified = new VerifiedPoint(state, native,
            new ReferenceIndexSnapshot(state.Revision, projection.Edges, projection.Records, limitations, schema, ct), snapshot, digest)
        {
            Anchors = anchors.ToArray(),
            ProjectId = projects.Length == 1 ? projects[0].Id : null,
            IndexTrusted = trusted
        };
        ct.ThrowIfCancellationRequested();
        PointAcquisitions++;
        if (trusted) point = verified;
        return verified;
    }

    /// <summary>
    /// The current verified point. A retained point is reused only while the published state row (revision and all
    /// digests) is unchanged; otherwise the full acquisition runs again. Cost of a reused point: one state read and
    /// one commit-hash read, independent of project size.
    /// </summary>
    /// <summary>Number of full verified acquisitions this handle has performed.</summary>
    internal int PointAcquisitions { get; private set; }

    /// <summary>The reference snapshot that planning and guarded writes currently reuse.</summary>
    internal async Task<ReferenceIndexSnapshot> PlanningIndexAsync() => (await PointAsync()).GetPlanningIndex();

    private Task<VerifiedPoint> PointAsync(CancellationToken ct = default) => ReadPublishedAsync(async state =>
        point is not null && point.State.SamePoint(state) ? point : await AcquireAsync(state, ct), ct);

    public async Task RebuildReferenceIndexAsync()
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database, Workers))
            throw new InvalidDataException("History is corrupt; the reference index cannot be republished.");
        await PublishIndexAsync(records, state.Revision);
    }

    /// <summary>Digests and index of records in hand; an import computes them while its rows are written.</summary>
    private sealed record PublishedWork(Task<SetDigest> Snapshot, Task<ReferenceProjection> Projection)
    {
        public static PublishedWork Start(IReadOnlyList<GenericRecord> records) =>
            new(Task.Run(() => SnapshotDigest(records)), Task.Run(() => ReferenceProjection.Build(records)));
    }

    private async Task PublishIndexAsync(IReadOnlyList<GenericRecord> records, string revision, PublishedWork? work = null)
    {
        point = null;
        var projection = work is null ? ReferenceProjection.Build(records) : await work.Projection;
        await using var transaction = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync() : null;
            await ReferencePersistence.ReplaceAsync(database, projection.Edges);
        var state = await database.Set<StoreState>().SingleAsync();
        state.Revision = revision;
        state.IndexDigest = projection.Digest();
        state.IndexLimitations = string.Join("\n", projection.Limitations);
        state.ReplayDigest = await ReplayIntegrity.DigestAsync(database, Workers);
        state.SnapshotDigest = (work is null ? SnapshotDigest(records) : await work.Snapshot).Final("snapshot");
        state.ContentDigest = content.Digest();
        state.Published = true;
        await database.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        database.ChangeTracker.Clear();
    }

    private async Task<StoreState> PublishedStateAsync(CancellationToken ct = default)
    {
        if (requiresReopen) throw new InvalidOperationException("A failed field write requires reopening this handle.");
        var state = await database.Set<StoreState>().AsNoTracking().SingleAsync(ct);
        if (!state.Published) throw new InvalidOperationException("The store import has not been published.");
        if (state.ContentDigest != content.Digest(ct))
        {
            if (!readOnly)
                throw new InvalidOperationException("The store was published through another handle; reopen this handle before reading.");
            var rows = await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct);
            content.Reload(rows, state.ContentDigest, ct);
        }
        if (state.Revision != await CommitRevisionAsync(ct)) throw new InvalidOperationException("The store index revision is unavailable.");
        return state;
    }

    private async Task<string> CommitRevisionAsync(CancellationToken ct = default)
    {
        var hashes = await database.Set<Commit>().AsNoTracking().Select(c => c.Hash).ToArrayAsync(ct);
        ct.ThrowIfCancellationRequested();
        Array.Sort(hashes, StringComparer.Ordinal);
        ct.ThrowIfCancellationRequested();
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes)));
        ct.ThrowIfCancellationRequested();
        return Convert.ToHexStringLower(digest);
    }

    internal static byte[] SnapshotItem(GenericRecord record)
    {
        if (RecordContent.HasDirectItem(record))
        {
            using var output = new BinaryOutput();
            RecordContent.WriteRecordItem(output, record);
            return output.Finish();
        }
        return SnapshotItemFromJson(record);
    }

    /// <summary>The reference definition of a digest item; the direct writer must produce exactly these bytes.</summary>
    internal static byte[] SnapshotItemFromJson(GenericRecord record)
    {
        var edited = record.Lists.Where(p => p.Value.Ops.Count > 0).OrderBy(p => p.Key).ToArray();
        // Unedited records keep their earlier digest shape; edited lists add their retained operations.
        if (edited.Length == 0) return RecordContent.Canonical(JsonSerializer.SerializeToElement(new
        {
            record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField, record.OwnOrdinal,
            record.IdentityFields, record.Layout, record.Values, record.Provenance, record.DeletedAt
        }));
        return RecordContent.Canonical(JsonSerializer.SerializeToElement(new
        {
            record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField, record.OwnOrdinal,
            record.IdentityFields, record.Layout, record.Values, record.Provenance, record.DeletedAt,
            Lists = edited.Select(p => new { Field = p.Key, p.Value.Initial, Operations = p.Value.Ops.Select(op => new { Json = SIL.ListCrdt.OpJson.ToJson(op), Seen = p.Value.Seen.TryGetValue(op.Id, out var view) && view.Count > 0 ? view.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new object[] { x.Key, x.Value }) : null }) })
        }));
    }

    internal static SetDigest SnapshotDigest(IEnumerable<GenericRecord> records, CancellationToken ct = default)
    {
        var digest = new SetDigest();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            digest = digest.Add(SnapshotItem(record));
        }
        return digest;
    }

    private static SetDigest VerifySnapshots(StoreState state, IReadOnlyList<GenericRecord> records, CancellationToken ct = default) =>
        VerifySnapshots(state, SnapshotDigest(records, ct));

    private static SetDigest VerifySnapshots(StoreState state, SetDigest digest)
    {
        if (state.SnapshotDigest != digest.Final("snapshot"))
            throw new InvalidDataException("The snapshot projection is corrupt; regenerate it from history before use.");
        return digest;
    }

    public async Task RegenerateAsync()
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database, Workers))
            throw new InvalidDataException("History is corrupt; the existing snapshot projection has been preserved.");
        var temporary = Directory.CreateTempSubdirectory("motif-record-replay-");
        try
        {
            var originalPath = database.Database.GetDbConnection().DataSource;
            var stagedPath = Path.Combine(temporary.FullName, "records.sqlite");
            await CopyDatabaseAsync(originalPath, stagedPath);
            await using (var candidate = new RecordStore(stagedPath))
            {
                await candidate.RegenerateVerifiedAsync();
                if ((await candidate.PublishedStateAsync()).ContentDigest != state.ContentDigest)
                    throw new InvalidDataException("Replay changed immutable content; the original store has been preserved.");
            }
            await CopyDatabaseAsync(stagedPath, originalPath);
            database.ChangeTracker.Clear();
        }
        finally { temporary.Delete(true); }
    }

    private async Task RegenerateVerifiedAsync()
    {
        var state = await PublishedStateAsync();
        await model.RegenerateSnapshots();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        await PublishIndexAsync(records, state.Revision);
    }

    private static async Task CopyDatabaseAsync(string sourcePath, string destinationPath)
    {
        await using var source = new SqliteConnection(RecordSqlite.ConnectionString(sourcePath));
        await source.OpenAsync();
        PublishedHomeConnection.KeepWal(source);
        RecordSqlite.BackupTo(source, destinationPath, PublishedHomeConnection.KeepWal);
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }
}
