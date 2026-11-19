using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class BulkImportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-bulk-" + Guid.NewGuid().ToString("N"));
    private string Database
    {
        get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); }
    }

    private static GenericRecord Sample(int seed, bool provenance = true, JsonElement? layout = null)
    {
        var id = Guid.NewGuid();
        var record = RecordInitialization.Create(id, "lcm", 5000 + seed % 3, seed % 2 == 0 ? Guid.NewGuid() : null, seed, seed, 0b1111,
            layout ?? RecordInitialization.EmptyLayout, provenance ? new NativeProvenance(seed, "traversal/" + seed,
                ImmutableDictionary<int, ImmutableArray<Guid>>.Empty.Add(20 + seed, [Guid.NewGuid(), Guid.NewGuid()]).Add(3, [])
                    .Add(1000, [Guid.NewGuid()]))
            { RepositoryOrdinals = ImmutableDictionary<string, long>.Empty.Add("LexEntry", seed).Add("text", 2) } : null);
        record.Values["101"] = JsonSerializer.SerializeToElement(id);
        record.Values["102"] = JsonSerializer.SerializeToElement(record.ClassId);
        record.Values["104"] = JsonSerializer.SerializeToElement(record.OwningField);
        record.Values["105"] = JsonSerializer.SerializeToElement(record.OwnOrdinal);
        record.Values["1001"] = JsonSerializer.SerializeToElement("plain " + seed);
        record.Values["1002"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("D").ToUpperInvariant());
        record.Values["1003"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        record.Values["1004"] = JsonSerializer.SerializeToElement(12345678901234L * seed);
        record.Values["1005"] = JsonDocument.Parse("1.50").RootElement.Clone();
        record.Values["1006"] = JsonSerializer.SerializeToElement(new { en = new { text = "x", utf16le = "eAA=" }, tl = "é́" });
        record.Values["1007"] = JsonSerializer.SerializeToElement(new { text = "x", utf16le = (string?)null });
        record.Values["1008"] = JsonSerializer.SerializeToElement(new { ints = Array.Empty<int>(), strings = Array.Empty<int>() });
        record.Values["1009"] = JsonSerializer.SerializeToElement(new object?[] { null, true, false, 3, "s" });
        return record;
    }

    [Fact]
    public void DirectDigestItemsEqualTheirJsonDefinitionAndTheDigestIsTheirSum()
    {
        var layout = JsonSerializer.SerializeToElement(new { version = 2, table = BuiltInLayouts.Version, types = new Dictionary<string, int>(), defaults = new Dictionary<string, int> { ["1001"] = 0 } });
        var records = Enumerable.Range(0, 5000).Select(n => Sample(n, provenance: n % 7 != 0, layout: n % 2 == 0 ? layout : null)).ToList();
        var twoKeys = Sample(1); twoKeys.Values.Clear(); twoKeys.Values["text"] = JsonSerializer.SerializeToElement("a"); twoKeys.Values["utf16le"] = JsonSerializer.SerializeToElement((string?)null);
        var deleted = Sample(2); deleted.DeletedAt = new DateTimeOffset(2026, 11, 19, 1, 2, 3, TimeSpan.Zero);
        records.AddRange([twoKeys, deleted, Sample(3), new GenericRecord { Id = Guid.NewGuid() }]);
        foreach (var record in records) Assert.Equal(Store.SnapshotItemFromJson(record), Store.SnapshotItem(record));
        var sequential = new SetDigest();
        foreach (var record in records) sequential = sequential.Add(Store.SnapshotItemFromJson(record));
        Assert.Equal(sequential, Store.SnapshotDigest(records));
        Assert.Equal(new SetDigest(), Store.SnapshotDigest([]));
    }

    [Fact]
    public void TypedContentPutsShareIdentityWithTheirJsonForms()
    {
        var content = new RecordContent();
        var guid = Guid.NewGuid();
        Assert.Equal(content.Put(JsonSerializer.SerializeToElement(guid)), content.PutGuid(guid));
        foreach (var text in new[] { "", "plain", guid.ToString("D"), guid.ToString("D").ToUpperInvariant(), guid.ToString("N"), "é" })
            Assert.Equal(content.Put(JsonSerializer.SerializeToElement(text)), content.PutText(text));
        var provenance = Sample(4).Provenance!;
        Assert.Equal(content.Put(JsonSerializer.SerializeToElement(provenance.Vectors)),
            content.PutBuilt(output => RecordContent.WriteVectors(output, provenance.Vectors)));
        Assert.Equal(content.Put(JsonSerializer.SerializeToElement(provenance.RepositoryOrdinals)),
            content.PutBuilt(output => RecordContent.WriteOrdinals(output, provenance.RepositoryOrdinals)));
        Assert.Equal(content.Put(JsonSerializer.SerializeToElement(ImmutableDictionary<int, ImmutableArray<Guid>>.Empty)),
            content.PutBuilt(output => RecordContent.WriteVectors(output, ImmutableDictionary<int, ImmutableArray<Guid>>.Empty)));
    }

    private static (RecordImport[] Imports, NativeImportCoverage Coverage) Fixture(int count)
    {
        var records = Enumerable.Range(0, count).Select(n => new GenericRecord { Id = Guid.NewGuid(), ClassId = 7 }).ToArray();
        var types = ImmutableDictionary<int, int>.Empty;
        var coverage = NativeImportCoverage.Capture(records.Select(r => new ReferenceRecord(r.Id, r.ClassId, null, 0)).ToArray(),
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(7, types), "native import fixture");
        return (records.Select(r => new RecordImport(r, types)).ToArray(), coverage);
    }

    [Fact]
    public async Task ACrashMidImportLeavesAnUnpublishedHomeThatCannotBeReadAndCanBeImportedAgain()
    {
        var (imports, coverage) = Fixture(1203);
        var copies = new List<string>();
        var batches = 0;
        // The backup API captures committed state only: the three copies are the same pre-import state.
        void Capture(string label)
        {
            var copy = Path.Combine(root, "crashed-" + label, "records.sqlite");
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            using var source = new SqliteConnection(RecordSqlite.ConnectionString(Database));
            using var target = new SqliteConnection("Data Source=" + copy);
            source.Open(); target.Open();
            source.BackupDatabase(target);
            copies.Add(copy);
        }
        NativeImportWriter.BatchWritten = path =>
        {
            if (path != Database) return;
            if (++batches is 2 or 3) Capture("batch" + batches);
        };
        NativeImportWriter.IndexesRebuilt = path => { if (path == Database) Capture("rebuilt"); };
        try
        {
            await using var store = new Store(Database);
            await store.ImportNativeAsync(imports, Guid.NewGuid(), coverage);
        }
        finally { NativeImportWriter.BatchWritten = null; NativeImportWriter.IndexesRebuilt = null; }
        Assert.Equal(3, batches);
        Assert.Equal(3, copies.Count);
        foreach (var copy in copies)
        {
            SqliteConnection.ClearAllPools();
            Assert.Contains("IX_Snapshots_EntityId", IndexNames(copy));
            Assert.Contains("IX_Snapshots_CommitId_EntityId", IndexNames(copy));
            Assert.Equal(0L, RowCount(copy, "Snapshots"));
            Assert.Equal(0L, RowCount(copy, "ChangeEntities"));
            await using var survivor = new Store(copy);
            Assert.Empty(await survivor.CommitsAsync());
            await Assert.ThrowsAsync<InvalidOperationException>(survivor.ReadAsync);
            await Assert.ThrowsAsync<InvalidOperationException>(() => survivor.ReferencesAsync());
        }
        await using (var again = new Store(copies[^1]))
        {
            await again.ImportNativeAsync(imports, Guid.NewGuid(), coverage);
            Assert.Equal(imports.Length + 1, (await again.ReadAsync()).Count);
        }
        await using var complete = new Store(Database);
        Assert.Equal(imports.Length + 1, (await complete.ReadAsync()).Count);
    }

    private static long RowCount(string database, string table)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(database));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return (long)command.ExecuteScalar()!;
    }

    private static List<string> IndexNames(string database)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(database));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL ORDER BY name";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        return names;
    }

    [Fact]
    public async Task OpeningAStoreCreatedBeforeTheIndexAddsItAndStillRefusesNewerHistory()
    {
        var (imports, coverage) = Fixture(30);
        await using (var store = new Store(Database))
            await store.ImportNativeAsync(imports, Guid.NewGuid(), coverage);
        SqliteConnection.ClearAllPools();
        Execute("DROP INDEX IX_ChangeEntities_AuthoredModelVersion");
        Assert.DoesNotContain("IX_ChangeEntities_AuthoredModelVersion", IndexNames(Database));
        await using (var upgraded = new Store(Database)) Assert.Equal(imports.Length + 1, (await upgraded.ReadAsync()).Count);
        SqliteConnection.ClearAllPools();
        Assert.Contains("IX_ChangeEntities_AuthoredModelVersion", IndexNames(Database));
        Execute("UPDATE ChangeEntities SET AuthoredModelVersion = 2 WHERE rowid = 1");
        await Assert.ThrowsAnyAsync<Exception>(async () => { await using var refused = new Store(Database); await refused.ReadAsync(); });
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(Database));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void ValuesNestedTooDeepAreRefusedByTheDirectWriterAsTheJsonDefinitionRefusesThem()
    {
        static JsonElement Nested(int levels)
        {
            var text = new string('[', levels) + "1" + new string(']', levels);
            return JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 200 }).RootElement.Clone();
        }
        var ok = Sample(1); ok.Values["1010"] = Nested(62);
        Assert.Equal(Store.SnapshotItemFromJson(ok), Store.SnapshotItem(ok));
        var tooDeep = Sample(2); tooDeep.Values["1010"] = Nested(63);
        Assert.ThrowsAny<Exception>(() => Store.SnapshotItemFromJson(tooDeep));
        Assert.ThrowsAny<Exception>(() => Store.SnapshotItem(tooDeep));
    }

    [Fact]
    public async Task PackedImportRebuildsEverySecondaryIndexAndIndexesTheAuthoredVersion()
    {
        var (imports, coverage) = Fixture(30);
        await using (var store = new Store(Database))
            await store.ImportNativeAsync(imports, Guid.NewGuid(), coverage);
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(Database));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL ORDER BY name";
        using var reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read()) names.Add(reader.GetString(0));
        Assert.Contains("IX_Snapshots_EntityId", names);
        Assert.Contains("IX_Snapshots_CommitId_EntityId", names);
        Assert.Contains("IX_ChangeEntities_AuthoredModelVersion", names);
        reader.Close();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT AuthoredModelVersion FROM ChangeEntities WHERE AuthoredModelVersion > 1";
        using var plan = command.ExecuteReader();
        var detail = "";
        while (plan.Read()) detail += plan.GetString(3);
        Assert.Contains("IX_ChangeEntities_AuthoredModelVersion", detail);
    }

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }
}
