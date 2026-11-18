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

public sealed class RecordStore : IAsyncDisposable
{
    public const int MaximumChangesPerCommit = 500;
    private readonly ServiceProvider provider;
    private readonly AsyncServiceScope scope;
    private readonly DataModel model;
    private readonly RecordDatabase database;
    private readonly RecordContent content;
    public IRecordList Lists { get; } = new IndexRecordList();

    public RecordStore(string path, TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        services.AddSingleton<RecordContent>();
        // Converters close over this store's immutable content cache; their EF model must have the same lifetime.
        services.AddDbContext<RecordDatabase>(options => options.UseSqlite(connection).EnableServiceProviderCaching(false));
        services.AddCrdtData<RecordDatabase>(config =>
        {
            config.EnableProjectedTables = false;
            config.ObjectTypeListBuilder.DefaultAdapter().Add<GenericRecord>();
            config.ChangeTypeListBuilder.Add<CreateRecord>().Add<SetRecordValue>()
                .Add<SetRecordAlternative>().Add<InsertRecordItem>();
            config.ModelVersionBuilder.Add(0, "Motif generic records", version => version
                .Change<CreateRecord>().Change<SetRecordValue>().Change<SetRecordAlternative>().Change<InsertRecordItem>());
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
                command.CommandText = created ? "PRAGMA user_version=2" : "PRAGMA user_version";
                if (created) command.ExecuteNonQuery();
                else if (Convert.ToInt32(command.ExecuteScalar()) != 2) throw new InvalidDataException("Unsupported record store format.");
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
        var prepared = materialized.GroupBy(r => (r.Record.Kind, r.Record.ClassId, Fields: JsonSerializer.Serialize(r.Record.Values.Keys.Order(StringComparer.Ordinal)))).SelectMany(group =>
        {
            var layout = RecordInitialization.Layout(group);
            return group.Select(r => new RecordImport(RecordInitialization.Prepare(r.Record, layout), r.FieldTypes));
        });
        await model.AddManyChanges(clientId, Changes(prepared), () => null, MaximumChangesPerCommit);
        database.ChangeTracker.Clear();
        await PublishIndexAsync(await ReadRecordsAsync(), await CommitRevisionAsync());
    }

    private static IEnumerable<IChange> Changes(IEnumerable<RecordImport> imports)
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

    public async Task<IReadOnlyList<GenericRecord>> ReadAsync()
    {
        var state = await PublishedStateAsync();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        return records;
    }

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
        var state = await PublishedStateAsync();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        var projection = new ReferenceProjection(await ReferencePersistence.ReadAsync(database), records.Where(r => r.Kind == "lcm")
            .Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)).ToArray(),
            state.IndexLimitations.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        var limitations = projection.Limitations.ToList();
        if (projection.Digest() != state.IndexDigest) limitations.Add("Incoming-reference index is corrupt; rebuild it before use.");
        return new ReferenceIndexSnapshot(state.Revision, projection.Edges, projection.Records, limitations, evidence);
    }

    public async Task RebuildReferenceIndexAsync()
    {
        var state = await PublishedStateAsync();
        var records = await ReadRecordsAsync();
        VerifySnapshots(state, records);
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database))
            throw new InvalidDataException("History is corrupt; the reference index cannot be republished.");
        await PublishIndexAsync(records, state.Revision);
    }

    private async Task PublishIndexAsync(IReadOnlyList<GenericRecord> records, string revision)
    {
        var projection = ReferenceProjection.Build(records);
        await using var transaction = await database.Database.BeginTransactionAsync();
        await ReferencePersistence.ReplaceAsync(database, projection.Edges);
        var state = await database.Set<StoreState>().SingleAsync();
        state.Revision = revision;
        state.IndexDigest = projection.Digest();
        state.IndexLimitations = string.Join("\n", projection.Limitations);
        state.ReplayDigest = await ReplayIntegrity.DigestAsync(database);
        state.SnapshotDigest = SnapshotDigest(records);
        state.ContentDigest = content.Digest();
        state.Published = true;
        await database.SaveChangesAsync();
        await transaction.CommitAsync();
        database.ChangeTracker.Clear();
    }

    private async Task<StoreState> PublishedStateAsync()
    {
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

    private string SnapshotDigest(IReadOnlyList<GenericRecord> records)
    {
        using var output = new BinaryOutput();
        output.Byte(1);
        output.Unsigned((ulong)records.Count);
        foreach (var record in records.OrderBy(r => r.Id)) output.Bytes(RecordContent.Canonical(JsonSerializer.SerializeToElement(new
        {
            record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField, record.OwnOrdinal,
            record.IdentityFields, record.Layout, record.Values, record.Provenance, record.DeletedAt
        })));
        return Convert.ToHexStringLower(SHA256.HashData(output.Finish()));
    }

    private void VerifySnapshots(StoreState state, IReadOnlyList<GenericRecord> records)
    {
        if (state.SnapshotDigest != SnapshotDigest(records))
            throw new InvalidDataException("The snapshot projection is corrupt; regenerate it from history before use.");
    }

    public async Task RegenerateAsync()
    {
        var state = await PublishedStateAsync();
        if (state.ReplayDigest != await ReplayIntegrity.DigestAsync(database))
            throw new InvalidDataException("History is corrupt; the existing snapshot projection has been preserved.");
        var temporary = Directory.CreateTempSubdirectory("motif-record-replay-");
        try
        {
            var original = database.Database.GetConnectionString()!;
            var stagedPath = Path.Combine(temporary.FullName, "records.sqlite");
            var staged = new SqliteConnectionStringBuilder(original) { DataSource = stagedPath }.ToString();
            await CopyDatabaseAsync(original, staged);
            await using (var candidate = new RecordStore(stagedPath))
            {
                await candidate.RegenerateVerifiedAsync();
                if ((await candidate.PublishedStateAsync()).ContentDigest != state.ContentDigest)
                    throw new InvalidDataException("Replay changed immutable content; the original store has been preserved.");
            }
            await CopyDatabaseAsync(staged, original);
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

    private static async Task CopyDatabaseAsync(string source, string destination)
    {
        await using var from = new SqliteConnection(source);
        await using var to = new SqliteConnection(destination);
        await from.OpenAsync();
        await to.OpenAsync();
        from.BackupDatabase(to);
    }

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }
}
