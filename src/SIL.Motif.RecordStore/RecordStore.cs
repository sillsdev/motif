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
    public IRecordList Lists { get; } = new IndexRecordList();

    public RecordStore(string path, TimeProvider? timeProvider = null)
    {
        var services = new ServiceCollection();
        var connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        services.AddDbContext<RecordDatabase>(options => options.UseSqlite(connection));
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
        database = scope.ServiceProvider.GetRequiredService<RecordDatabase>();
        database.Database.EnsureCreated();
        model = scope.ServiceProvider.GetRequiredService<DataModel>();
    }

    public static Guid AuxiliaryId(string kind, string identity)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("motif-record-store/" + kind + "/" + identity)));
        return Guid.ParseExact(hex[..32], "N");
    }

    public async Task ImportAsync(IEnumerable<RecordImport> records, Guid clientId)
    {
        if (await database.Set<Commit>().AnyAsync()) throw new InvalidOperationException("Import requires an empty store.");
        var materialized = records.ToArray();
        if (materialized.Select(r => r.Record.Id).Distinct().Count() != materialized.Length)
            throw new InvalidDataException("Duplicate record identity in import.");
        await model.AddManyChanges(clientId, Changes(materialized), () => null, MaximumChangesPerCommit);
        database.ChangeTracker.Clear();
    }

    private static IEnumerable<IChange> Changes(IEnumerable<RecordImport> imports)
    {
        foreach (var import in imports)
        {
            var record = import.Record;
            yield return new CreateRecord(record.Id, record.Kind, record.ClassId, record.Owner, record.OwningField);
            foreach (var (key, value) in record.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var type = int.TryParse(key, out var field) ? import.FieldTypes.GetValueOrDefault(field) : 0;
                if (type is 14 or 16)
                {
                    yield return new SetRecordValue(record.Id, key, JsonSerializer.SerializeToElement(new Dictionary<string, object>()));
                    foreach (var alternative in value.EnumerateObject())
                        yield return new SetRecordAlternative(record.Id, field, alternative.Name, alternative.Value);
                }
                else if (type >= 25)
                {
                    yield return new SetRecordValue(record.Id, key, JsonSerializer.SerializeToElement(Array.Empty<Guid>()));
                    var index = 0;
                    foreach (var item in value.EnumerateArray()) yield return new InsertRecordItem(record.Id, field, item.GetGuid(), index++);
                }
                else yield return new SetRecordValue(record.Id, key, value);
            }
        }
    }

    public async Task<IReadOnlyList<GenericRecord>> ReadAsync()
    {
        var records = new List<GenericRecord>();
        await foreach (var snapshot in model.GetLatestSnapshots())
            if (snapshot.Entity is GenericRecord { DeletedAt: null } record) records.Add((GenericRecord)record.Copy());
        return records;
    }

    public async Task<IReadOnlyList<RecordCommit>> CommitsAsync() => await database.Set<Commit>().AsNoTracking()
        .Select(c => new RecordCommit(c.Id, c.HybridDateTime.DateTime, c.HybridDateTime.Counter, c.ChangeEntities.Count))
        .ToListAsync();

    public async Task RegenerateAsync() => await model.RegenerateSnapshots();

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await provider.DisposeAsync();
    }
}
