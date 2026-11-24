using SIL.Harmony.Changes;
using SIL.Motif.RecordStore.HandAnalysis;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using SIL.Harmony;
using SIL.Harmony.Core;

namespace SIL.Motif.RecordStore;

public sealed record RecordImport(GenericRecord Record, IReadOnlyDictionary<int, int> FieldTypes);
public sealed record RecordCommit(Guid Id, DateTimeOffset UtcTime, long Counter, int ChangeCount);

public sealed partial class RecordStore : IAsyncDisposable
{
    public const int MaximumChangesPerCommit = 500;
    // Format 6 adds the certificate, format 7 the derived tables; a first read-write operation upgrades.
    private const int StoreFormat = 7;
    private const int CertificateFormat = 6;
    private const int PackedStoreFormat = 5;
    private const int LegacyStoreFormat = 4;
    internal const int CompleteRecordModelVersion = 1;
    private int format;
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

    /// <summary>Opens as a reader that predates a format would: anything newer than <paramref name="highestFormat"/> is refused.</summary>
    internal static RecordStore OpenWithFormatCeiling(string path, int highestFormat) =>
        new(ResolvePath(path), null, false, highestFormat);

    internal static RecordStore OpenPublishedReader(string resolvedPath) => new(resolvedPath, null, true);

    private RecordStore(string path, TimeProvider? timeProvider, bool readOnly, int highestFormat = StoreFormat)
    {
        this.readOnly = readOnly;
        writeLockPath = path + ".write.lock";
        using var writer = readOnly ? null : AcquireWriter();
        var services = new ServiceCollection();
        var connection = RecordSqlite.ConnectionString(path, readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate);
        var storedFormat = File.Exists(path) ? ReadFormat(connection) : 0;
        if (storedFormat is not (0 or LegacyStoreFormat or PackedStoreFormat or CertificateFormat or StoreFormat) || storedFormat > highestFormat)
            throw new InvalidDataException("Unsupported record store format.");
        // A new file is created in the current format; an existing one keeps its own until its first write upgrades it.
        format = storedFormat == 0 ? StoreFormat : storedFormat;
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
                database.Database.ExecuteSqlRaw(IdentityTable);
                CreateDerivedTables(database);
                database.Add(new StoreState());
                database.SaveChanges();
            }
            var state = database.Set<StoreState>().AsNoTracking().Single();
            if (readOnly && !state.Published) throw new InvalidDataException("The store import has not been published.");
            if (!readOnly && state.Published && state.ContentDigest != content.Digest())
                throw new InvalidDataException("Corrupt immutable record content.");
            UseIdentityTable();
            if (readOnly && format >= DerivedFormat) UseDemandedContent();
            Verification = OpenVerification(state);
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
        ImportAsync(records, clientId, coverage, packed: format >= PackedStoreFormat);

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
            await UpgradeInTransactionAsync();
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
            await PublishIndexAsync(baseline ?? await ReadRecordsAsync(), await CommitRevisionAsync(), work, CertificateProvenance.FullImport,
                refuseUnprovenNative: true);
            await transaction.CommitAsync();
            CompleteUpgrade();
        }
        catch
        {
            requiresReopen = true;
            AbandonUpgrade();
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

    private sealed record DecodedSnapshots(IReadOnlyList<GenericRecord> Records, SetDigest Digest, RecordHeader[] Tombstones,
        DerivedSnapshot? Derived);

    /// <summary>Live records in Harmony's order, decoded on workers; derived adds chunks and headers.</summary>
    private async Task<DecodedSnapshots> DecodeLatestAsync(bool digest, CancellationToken ct, bool derived = false)
    {
        derived &= digest;
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
        var partialChunks = derived ? new Dictionary<int, SetDigest>[chunks] : null;
        var headers = derived ? new RecordHeader[blobs.Count] : null;
        var tombstones = new RecordHeader?[blobs.Count];
        var workers = Workers ?? ParallelWork.DefaultWorkers;
        await Task.Run(() => ParallelWork.ForEachChunk(chunks, workers, ct, chunk =>
        {
            DecodeChunkStarted?.Invoke(chunk);
            var sum = new SetDigest();
            var parts = derived ? new Dictionary<int, SetDigest>() : null;
            var end = Math.Min(blobs.Count, (chunk + 1) * size);
            for (var index = chunk * size; index < end; index++)
            {
                ct.ThrowIfCancellationRequested();
                var record = (GenericRecord)codec.Snapshot(blobs[index]);
                if (headers is not null) headers[index] = RecordHeader.From(record);
                if (record.DeletedAt is not null) { tombstones[index] = RecordHeader.From(record); continue; }
                decoded[index] = record;
                if (!digest) continue;
                var item = SnapshotItem(record);
                sum = sum.Add(item);
                if (parts is not null)
                {
                    var key = ChunkOf(record.Id);
                    parts[key] = parts.GetValueOrDefault(key).Add(item);
                }
            }
            partial[chunk] = sum;
            if (partialChunks is not null) partialChunks[chunk] = parts!;
        }), ct);
        SnapshotsDecoded += blobs.Count;
        var records = new List<GenericRecord>(blobs.Count);
        var seen = new HashSet<Guid>(blobs.Count);
        foreach (var record in decoded) if (record is not null) records.Add(record);
        foreach (var id in records.Select(r => r.Id).Concat(tombstones.Where(t => t is not null).Select(t => t!.Value.Id)))
            if (!seen.Add(id)) throw new InvalidDataException($"Snapshots: two latest snapshots decode to record {id}.");
        var total = new SetDigest();
        foreach (var part in partial) total = total.Combine(part);
        DerivedSnapshot? derivedTables = null;
        if (partialChunks is not null)
        {
            var sums = new SetDigest[ChunkCount];
            foreach (var parts in partialChunks)
                foreach (var (key, value) in parts) sums[key] = sums[key].Combine(value);
            derivedTables = new DerivedSnapshot(sums, headers!);
        }
        return new(records, total, tombstones.Where(t => t is not null).Select(t => t!.Value).ToArray(), derivedTables);
    }

    public async Task<IReadOnlyList<RecordCommit>> CommitsAsync() => await database.Set<Commit>().AsNoTracking()
        .Select(c => new RecordCommit(c.Id, c.HybridDateTime.DateTime, c.HybridDateTime.Counter, c.ChangeEntities.Count))
        .ToListAsync();

    public async Task<ReferenceIndexSnapshot> ReferencesAsync(ReferenceEvidence? evidence = null)
    {
        var (_, index) = await PlanningPointAsync();
        return evidence is null ? index : index.WithObservations(evidence);
    }

    /// <summary>Reads and verifies the published point; a trusted result is retained for reuse.</summary>
    private async Task<VerifiedPoint> AcquireAsync(StoreState state, CancellationToken ct = default, Action<string, string>? observe = null)
    {
        point = null;
        // A verification (observe) always decodes and digests everything; so does any point that cannot be certified.
        var downgraded = false; // derived tables disagreed with a trusted certificate: vouch only after a history scan
        if (observe is null && CertifiedAcquisitionAllowed && format >= DerivedFormat && OpenVerification(state) == StoreVerification.Certified &&
            !state.IndexLimitations.Contains(NativeUsageSchema.ProvenanceLimitation, StringComparison.Ordinal))
        {
            if (await DerivedFindingAsync(state, ct) is null && await AcquireCertifiedAsync(state, ct) is { } certified) return certified;
            downgraded = true;
        }
        await EnsureContentAsync(state, ct);
        var decoded = await DecodeLatestAsync(digest: true, ct, derived: observe is not null || !readOnly && format < DerivedFormat);
        var records = decoded.Records;
        // A verification reports the digests it computed instead of refusing at the first mismatch.
        observe?.Invoke("snapshot", decoded.Digest.Final("snapshot"));
        var snapshot = observe is null ? VerifySnapshots(state, decoded.Digest) : decoded.Digest;
        var native = new Dictionary<Guid, GenericRecord>();
        var headers = new List<ReferenceRecord>();
        var anchors = new List<GenericRecord>();
        var systems = new List<GenericRecord>();
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            if (record.Kind == "lcm")
            {
                native.Add(record.Id, record);
                headers.Add(new(record.Id, record.ClassId, record.Owner, record.OwningField));
            }
            else if (record.Kind == HandAnalysisKinds.Anchor) anchors.Add(record);
            else if (record.Kind == "writing-system") systems.Add(record);
        }
        var projection = new ReferenceProjection(await ReferencePersistence.ReadAsync(database, ct), headers.ToArray(),
            state.IndexLimitations.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var limitations = projection.Limitations.ToList();
        var digest = projection.Accumulate(ct);
        observe?.Invoke("index", digest.Final());
        var trusted = digest.Final() == state.IndexDigest;
        if (!trusted) limitations.Add("Incoming-reference index is corrupt; rebuild it before use.");
        var schema = NativeUsageSchema.Read(records, ct);
        var projectClass = schema?.ClassId("LangProject");
        var projects = projectClass.HasValue ? headers.Where(r =>
        {
            ct.ThrowIfCancellationRequested();
            return r.ClassId == projectClass;
        }).Take(2).ToArray() : [];
        var verified = new VerifiedPoint(state, RecordSource.Eager(native),
            new ReferenceIndexSnapshot(state.Revision, projection.Edges, projection.Records, limitations, schema, ct), snapshot, digest)
        {
            Anchors = anchors.ToArray(),
            WritingSystems = systems.ToArray(),
            ProjectId = projects.Length == 1 ? projects[0].Id : null,
            IndexTrusted = trusted,
            Derived = decoded.Derived,
            UnprovenNative = decoded.Derived is null ? null : NativeUsageSchema.ProvenanceFailure(records)
        };
        ct.ThrowIfCancellationRequested();
        PointAcquisitions++;
        if (observe is null)
        {
            // Another handle may have written since this one last looked; what it reads again is not what it verified.
            Verification = downgraded ? StoreVerification.Uncertified : OpenVerification(state);
            if (trusted) point = verified;
        }
        return verified;
    }

    /// <summary>
    /// The current verified point. A retained point is reused only while the published state row (revision and all
    /// digests) is unchanged; otherwise the full acquisition runs again. Cost of a reused point: one state read and
    /// one commit-hash read, independent of project size.
    /// </summary>
    /// <summary>Number of full verified acquisitions this handle has performed.</summary>
    internal int PointAcquisitions { get; private set; }

    /// <summary>Times the in-memory identity dictionary was built over every content row.</summary>
    internal int ContentIdentityDictionaryBuilds => content.IdentityBuilds;

    /// <summary>The reference snapshot that planning and guarded writes currently reuse.</summary>
    internal async Task<ReferenceIndexSnapshot> PlanningIndexAsync() => (await PlanningPointAsync()).Index;

    // The point and its planning index come from one read transaction, so no publication can fall between them.
    private Task<(VerifiedPoint Point, ReferenceIndexSnapshot Index)> PlanningPointAsync(CancellationToken ct = default) =>
        ReadPublishedAsync(async state =>
        {
            var served = point is not null && point.State.SamePoint(state) ? point : await AcquireAsync(state, ct);
            return (served, await served.GetPlanningIndexAsync(ct));
        }, ct, lazyContent: true);

    private Task<VerifiedPoint> PointAsync(CancellationToken ct = default) => ReadPublishedAsync(async state =>
        point is not null && point.State.SamePoint(state) ? point : await AcquireAsync(state, ct), ct, lazyContent: true);

    /// <summary>Cancelling before the single publishing transaction starts changes nothing; the transaction itself is not interrupted.</summary>
    public async Task RebuildReferenceIndexAsync(CancellationToken ct = default)
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync(ct);
        var latest = await DecodeLatestAsync(digest: true, ct);
        VerifySnapshots(state, latest.Digest);
        if (state.ReplayDigest != (await ReplayIntegrity.ScanAsync(database, Workers, ct: ct)).Final())
            throw new InvalidDataException("History is corrupt; the reference index cannot be republished.");
        ct.ThrowIfCancellationRequested();
        await PublishIndexAsync(latest.Records, state.Revision, tombstones: latest.Tombstones);
    }

    /// <summary>Digests and index of records in hand; an import computes them while its rows are written.</summary>
    private sealed record PublishedWork(Task<SetDigest[]> Snapshot, Task<ReferenceProjection> Projection)
    {
        public static PublishedWork Start(IReadOnlyList<GenericRecord> records) =>
            new(Task.Run(() => ChunkDigests(records)), Task.Run(() => ReferenceProjection.Build(records)));
    }

    private async Task PublishIndexAsync(IReadOnlyList<GenericRecord> records, string revision, PublishedWork? work = null,
        string provenance = CertificateProvenance.FullVerify, IReadOnlyList<RecordHeader>? tombstones = null,
        bool refuseUnprovenNative = false)
    {
        point = null;
        derivedChecked = null;
        var chunks = work is null ? ChunkDigests(records) : await work.Snapshot;
        var projection = work is null ? ReferenceProjection.Build(records) : await work.Projection;
        if (LegacyProvenanceForTests)
            projection = projection with { Limitations = projection.Limitations.Where(l => !l.StartsWith(NativeUsageSchema.ProvenanceLimitation, StringComparison.Ordinal)).ToArray() };
        await using var transaction = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync() : null;
        try
        {
            var state = await database.Set<StoreState>().SingleAsync();
            // New native records must pass the provenance proof; a store already failing it keeps the limitation.
            if (refuseUnprovenNative && !LegacyProvenanceForTests && !state.IndexLimitations.Contains(NativeUsageSchema.ProvenanceLimitation, StringComparison.Ordinal) &&
                projection.Limitations.FirstOrDefault(l => l.StartsWith(NativeUsageSchema.ProvenanceLimitation, StringComparison.Ordinal)) is { } unproven)
                throw new InvalidDataException(unproven);
            await UpgradeInTransactionAsync();
            // Tombstone headers are read only when derived tables are written.
            var writeDerived = format >= DerivedFormat || backfillDerived;
            if (writeDerived) tombstones ??= await ReadTombstonesAsync();
            await ReferencePersistence.ReplaceAsync(database, projection.Edges);
            state.Revision = revision;
            state.IndexDigest = projection.Digest();
            state.IndexLimitations = string.Join("\n", projection.Limitations);
            HistoryScans++;
            var replay = await ReplayIntegrity.ScanAsync(database, Workers);
            state.ReplayDigest = replay.Final();
            state.SnapshotDigest = Total(chunks).Final("snapshot");
            state.ContentDigest = content.Digest();
            state.Published = true;
            await StageCertificateAsync(state, replay, provenance);
            // Every full publication rewrites the derived tables; they are bound to this state row's digests.
            if (writeDerived)
                WriteDerived(state, chunks, records.Select(RecordHeader.From).Concat(tombstones!).ToArray());
            await database.SaveChangesAsync();
            if (transaction is not null)
            {
                await transaction.CommitAsync();
                CompleteUpgrade();
            }
        }
        catch
        {
            if (transaction is not null) AbandonUpgrade();
            throw;
        }
        database.ChangeTracker.Clear();
        Verification = StoreVerification.Full;
    }

    private async Task<StoreState> PublishedStateAsync(CancellationToken ct = default, bool lazyContent = false)
    {
        if (requiresReopen) throw new InvalidOperationException("A failed field write requires reopening this handle.");
        var state = await database.Set<StoreState>().AsNoTracking().SingleAsync(ct);
        if (!state.Published) throw new InvalidOperationException("The store import has not been published.");
        if (state.ContentDigest != content.Digest(ct))
        {
            if (!readOnly)
                throw new InvalidOperationException("The store was published through another handle; reopen this handle before reading.");
            // A certified point reads the few rows it needs by id; every other reader loads and checks them all.
            if (!lazyContent) await ReloadContentAsync(state, ct);
        }
        if (state.Revision != await CommitRevisionAsync(ct)) throw new StoreRevisionUnavailableException();
        return state;
    }

    private async Task ReloadContentAsync(StoreState state, CancellationToken ct)
    {
        var rows = await database.Set<ContentRow>().AsNoTracking().ToArrayAsync(ct);
        content.Reload(rows, state.ContentDigest, ct);
    }

    // A reader that served a certified point must hold every content row before a full verification.
    private async Task EnsureContentAsync(StoreState state, CancellationToken ct)
    {
        if (readOnly && state.ContentDigest != content.Digest(ct)) await ReloadContentAsync(state, ct);
    }

    // A format 7 reader may fetch a content row by id, checked against the identity table.
    private void UseDemandedContent()
    {
        content.PersistedLookup = LookupIdentity;
        content.RowReader = id =>
        {
            var connection = (SqliteConnection)database.Database.GetDbConnection();
            database.Database.OpenConnection();
            try
            {
                using var command = connection.CreateCommand();
                command.Transaction = database.Database.CurrentTransaction?.GetDbTransaction() as SqliteTransaction;
                command.CommandText = "SELECT Data FROM RecordContent WHERE Id = $id";
                command.Parameters.AddWithValue("$id", id);
                return command.ExecuteScalar() as byte[];
            }
            finally { database.Database.CloseConnection(); }
        };
    }

    /// <summary>The state row's revision is not the revision of the commits it was published for.</summary>
    internal sealed class StoreRevisionUnavailableException() : InvalidOperationException("The store index revision is unavailable.");

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
            RefreshFormat(originalPath);
            database.ChangeTracker.Clear();
        }
        finally { temporary.Delete(true); }
    }

    private async Task RegenerateVerifiedAsync()
    {
        var state = await PublishedStateAsync();
        await model.RegenerateSnapshots();
        var latest = await DecodeLatestAsync(digest: true, default);
        VerifySnapshots(state, latest.Digest);
        await PublishIndexAsync(latest.Records, state.Revision, tombstones: latest.Tombstones);
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
