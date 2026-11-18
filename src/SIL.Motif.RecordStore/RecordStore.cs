using SIL.Harmony.Changes;
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
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly DataModel model;
    internal DataModel Model => model;
    private readonly RecordDatabase database;
    private readonly RecordContent content;
    public static SIL.ListCrdt.ListLimits ListLimits => StoreListLimits.Default;
    public IRecordList Lists { get; } = new CrdtRecordList();

    public RecordStore(string path, TimeProvider? timeProvider = null)
    {
        path = ResolvePath(path);
        writeLockPath = path + ".write.lock";
        using var writer = AcquireWriter();
        var services = new ServiceCollection();
        var connection = RecordSqlite.ConnectionString(path);
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
                .Change<CreateRecord>().Change<SetRecordValue>().Change<SetRecordAlternative>().Change<InsertRecordItem>().Change<ListRecordOp>());
        });
        services.AddSingleton<TimeProvider>(new MicrosecondTimeProvider(timeProvider ?? TimeProvider.System));
        provider = services.BuildServiceProvider();
        scope = provider.CreateAsyncScope();
        try
        {
            database = scope.ServiceProvider.GetRequiredService<RecordDatabase>();
            var created = database.Database.EnsureCreated();
            using (var command = database.Database.GetDbConnection().CreateCommand())
            {
                database.Database.OpenConnection();
                command.CommandText = created ? "PRAGMA user_version=4" : "PRAGMA user_version";
                if (created) command.ExecuteNonQuery();
                else if (Convert.ToInt32(command.ExecuteScalar()) != 4) throw new InvalidDataException("Unsupported record store format.");
                database.Database.CloseConnection();
            }
            content = scope.ServiceProvider.GetRequiredService<RecordContent>();
            content.Load(database.Set<ContentRow>().AsNoTracking().ToArray());
            if (created)
            {
                ReferencePersistence.Create(database);
                database.Add(new StoreState());
                database.SaveChanges();
            }
            var state = database.Set<StoreState>().AsNoTracking().Single();
            if (state.Published && state.ContentDigest != content.Digest()) throw new InvalidDataException("Corrupt immutable record content.");
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

    public static Guid AuxiliaryId(string kind, string identity)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store/" + kind + "/" + identity)));
        return Guid.ParseExact(hex[..32], "N");
    }

    public async Task ImportAsync(IEnumerable<RecordImport> records, Guid clientId, NativeImportCoverage? nativeCoverage = null)
    {
        using var writer = AcquireWriter();
        point = null;
        if ((await database.Set<StoreState>().AsNoTracking().SingleAsync()).Published || await database.Set<Commit>().AnyAsync()) throw new InvalidOperationException("Import requires an empty store.");
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
        var layouts = materialized.GroupBy(r => (r.Record.Kind, r.Record.ClassId, Fields: JsonSerializer.Serialize(r.Record.Values.Keys.Order(StringComparer.Ordinal))))
            .Select(group => (Group: group, Layout: RecordInitialization.Layout(group))).ToList();
        var prepared = layouts.SelectMany(entry => entry.Group.Select(r => new RecordImport(RecordInitialization.Prepare(r.Record, entry.Layout), r.FieldTypes)));
        await model.AddManyChanges(clientId, Changes(prepared), () => null, MaximumChangesPerCommit);
        database.ChangeTracker.Clear();
        await PublishIndexAsync(await ReadRecordsAsync(), await CommitRevisionAsync());
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
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        return records;
    });

    private async Task<IReadOnlyList<GenericRecord>> ReadRecordsAsync()
    {
        var records = new List<GenericRecord>();
        await foreach (var snapshot in model.GetLatestSnapshots())
            if (snapshot.Entity is GenericRecord { DeletedAt: null } record) records.Add((GenericRecord)record.Copy());
        return records;
    }

    public async Task<IReadOnlyList<RecordCommit>> CommitsAsync() => await database.Set<Commit>().AsNoTracking()
        .Select(c => new RecordCommit(c.Id, c.HybridDateTime.DateTime, c.HybridDateTime.Counter, c.ChangeEntities.Count))
        .ToListAsync();

    public async Task<ReferenceIndexSnapshot> ReferencesAsync(ReferenceEvidence? evidence = null)
    {
        var acquired = await ReadPublishedAsync(state => AcquireAsync(state));
        return evidence is null ? acquired.Index : acquired.Index.WithEvidence(evidence);
    }

    /// <summary>Reads and verifies the published point; a trusted result is retained for reuse.</summary>
    private async Task<VerifiedPoint> AcquireAsync(StoreState state)
    {
        point = null;
        var records = await ReadRecordsAsync();
        var snapshot = VerifySnapshots(state, records);
        var projection = new ReferenceProjection(await ReferencePersistence.ReadAsync(database), records.Where(r => r.Kind == "lcm")
            .Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)).ToArray(),
            state.IndexLimitations.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var limitations = projection.Limitations.ToList();
        var digest = projection.Accumulate();
        var trusted = digest.Final() == state.IndexDigest;
        if (!trusted) limitations.Add("Incoming-reference index is corrupt; rebuild it before use.");
        var schema = NativeUsageSchema.Read(records);
        var verified = new VerifiedPoint(state, records.Where(r => r.Kind == "lcm").ToDictionary(r => r.Id),
            new ReferenceIndexSnapshot(state.Revision, projection.Edges, projection.Records, limitations, null, schema), snapshot, digest);
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
    internal async Task<ReferenceIndexSnapshot> PlanningIndexAsync() => (await PointAsync()).Index;

    private Task<VerifiedPoint> PointAsync() => ReadPublishedAsync(async state =>
        point is not null && point.State.SamePoint(state) ? point : await AcquireAsync(state));

    public async Task RebuildReferenceIndexAsync()
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database))
            throw new InvalidDataException("History is corrupt; the reference index cannot be republished.");
        await PublishIndexAsync(records, state.Revision);
    }

    private async Task PublishIndexAsync(IReadOnlyList<GenericRecord> records, string revision)
    {
        point = null;
        var projection = ReferenceProjection.Build(records);
        await using var transaction = database.Database.CurrentTransaction is null ? await database.Database.BeginTransactionAsync() : null;
        await ReferencePersistence.ReplaceAsync(database, projection.Edges);
        var state = await database.Set<StoreState>().SingleAsync();
        state.Revision = revision;
        state.IndexDigest = projection.Digest();
        state.IndexLimitations = string.Join("\n", projection.Limitations);
        state.ReplayDigest = await ReplayIntegrity.DigestAsync(database);
        state.SnapshotDigest = SnapshotDigest(records).Final("snapshot");
        state.ContentDigest = content.Digest();
        state.Published = true;
        await database.SaveChangesAsync();
        if (transaction is not null) await transaction.CommitAsync();
        database.ChangeTracker.Clear();
    }

    private async Task<StoreState> PublishedStateAsync()
    {
        if (requiresReopen) throw new InvalidOperationException("A failed field write requires reopening this handle.");
        var state = await database.Set<StoreState>().AsNoTracking().SingleAsync();
        if (!state.Published) throw new InvalidOperationException("The store import has not been published.");
        if (state.ContentDigest != content.Digest())
            throw new InvalidOperationException("The store was published through another handle; reopen this handle before reading.");
        if (state.Revision != await CommitRevisionAsync()) throw new InvalidOperationException("The store index revision is unavailable.");
        return state;
    }

    private async Task<string> CommitRevisionAsync()
    {
        var hashes = await database.Set<Commit>().AsNoTracking().Select(c => c.Hash).ToArrayAsync();
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", hashes.Order(StringComparer.Ordinal)))));
    }

    internal static byte[] SnapshotItem(GenericRecord record)
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

    private static SetDigest SnapshotDigest(IEnumerable<GenericRecord> records)
    {
        var digest = new SetDigest();
        foreach (var record in records) digest = digest.Add(SnapshotItem(record));
        return digest;
    }

    private static SetDigest VerifySnapshots(StoreState state, IReadOnlyList<GenericRecord> records)
    {
        var digest = SnapshotDigest(records);
        if (state.SnapshotDigest != digest.Final("snapshot"))
            throw new InvalidDataException("The snapshot projection is corrupt; regenerate it from history before use.");
        return digest;
    }

    public async Task RegenerateAsync()
    {
        using var writer = AcquireWriter();
        point = null;
        var state = await PublishedStateAsync();
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database))
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
        RecordSqlite.BackupTo(source, destinationPath);
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }
}
