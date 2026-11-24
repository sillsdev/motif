using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class StorageContractTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-format-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name) { Directory.CreateDirectory(root); return Path.Combine(root, name + ".sqlite"); }

    [Fact]
    public async Task IndependentStoresAndDifferentFieldLayoutsNeverShareConverterState()
    {
        var first = PathFor("first");
        var second = PathFor("second");
        var a = Record("a");
        var b = Record("b");
        b.Values["different"] = JsonSerializer.SerializeToElement(false);
        await using (var left = new Store(first))
        await using (var right = new Store(second))
        {
            await left.ImportAsync([new(a, new Dictionary<int, int>())], Guid.NewGuid());
            await right.ImportAsync([new(b, new Dictionary<int, int>())], Guid.NewGuid());
            await left.RegenerateAsync();
            Assert.Equal("a", Assert.Single(await left.ReadAsync()).Values["text"].GetString());
            Assert.Equal("b", Assert.Single(await right.ReadAsync()).Values["text"].GetString());
        }
        await using var reopened = new Store(first);
        Assert.False(Assert.Single(await reopened.ReadAsync()).Values.ContainsKey("different"));
    }

    [Fact]
    public async Task HistoryAndSnapshotsShareImmutableContentAndGuidColumnsUseNetworkOrder()
    {
        var path = PathFor("shared");
        var value = Record(Guid.NewGuid().ToString());
        await using (var store = new Store(path))
            await store.ImportAsync([new(value, new Dictionary<int, int>())], Guid.NewGuid());
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hex(EntityId), typeof(EntityId), length(EntityId), typeof(Change) FROM ChangeEntities LIMIT 1";
        using (var rows = command.ExecuteReader())
        {
            Assert.True(rows.Read());
            Assert.Equal(value.Id.ToString("N").ToUpperInvariant(), rows.GetString(0));
            Assert.Equal("blob", rows.GetString(1));
            Assert.Equal(16, rows.GetInt32(2));
            Assert.Equal("blob", rows.GetString(3));
        }
        command.CommandText = "SELECT count(*) FROM RecordContent WHERE Data IN (SELECT Data FROM RecordContent GROUP BY Data HAVING count(*) > 1)";
        Assert.Equal(0L, command.ExecuteScalar());
        command.CommandText = "UPDATE RecordContent SET Data = zeroblob(length(Data)) WHERE Id = (SELECT max(Id) FROM RecordContent)";
        command.ExecuteNonQuery();
        Assert.Throws<InvalidDataException>(() => new Store(path));
    }

    [Fact]
    public async Task LargeImmutableValueOccursOnceAcrossFieldsHistoryAndRegeneratedSnapshots()
    {
        var path = PathFor("once");
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(65536);
        var value = Record(Convert.ToBase64String(bytes));
        value.Values["other"] = value.Values["text"];
        await using (var store = new Store(path))
        {
            await store.ImportAsync([new(value, new Dictionary<int, int>())], Guid.NewGuid());
            await store.RegenerateAsync();
            Assert.Equal(bytes, Convert.FromBase64String(Assert.Single(await store.ReadAsync()).Values["text"].GetString()!));
        }
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sum(length(Data)) FROM RecordContent";
        Assert.InRange(Convert.ToInt64(command.ExecuteScalar()), bytes.Length, bytes.Length + 2048);
        command.CommandText = "SELECT sum(length(Entity)) FROM Snapshots";
        Assert.InRange(Convert.ToInt64(command.ExecuteScalar()), 1, 1024);
        command.CommandText = "SELECT sum(length(Change)) FROM ChangeEntities";
        Assert.InRange(Convert.ToInt64(command.ExecuteScalar()), 1, 1024);
    }

    [Fact]
    public async Task UnsupportedStoreAndChangeVersionsAreRefused()
    {
        var path = PathFor("version");
        await using (var store = new Store(path))
            await store.ImportAsync([new(Record("value"), new Dictionary<int, int>())], Guid.NewGuid());
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=999";
            command.ExecuteNonQuery();
            Assert.Throws<InvalidDataException>(() => new Store(path));
            command.CommandText = "PRAGMA user_version=5";
            command.ExecuteNonQuery();
            command.CommandText = "UPDATE ChangeEntities SET Change = x'ff'";
            command.ExecuteNonQuery();
        }
        await using var reopened = new Store(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.RegenerateAsync());
    }

    [Fact]
    public async Task PackedHistoryUsesTheNewAuthoredVersionAndFutureHistoryIsRefusedOnOpen()
    {
        var path = PathFor("native-version");
        var record = Record("value");
        var coverage = NativeImportCoverage.Capture([], ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty,
            "native import fixture");
        await using (var store = new Store(path))
            await store.ImportNativeAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid(), coverage);

        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DISTINCT AuthoredModelVersion FROM ChangeEntities";
        using (var reader = command.ExecuteReader())
        {
            Assert.True(reader.Read());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.False(reader.Read());
        }
        command.CommandText = "UPDATE ChangeEntities SET AuthoredModelVersion = 2";
        command.ExecuteNonQuery();
        var refusal = Assert.Throws<InvalidDataException>(() => new Store(path));
        Assert.Contains("model version", refusal.Message);
    }

    [Fact]
    public async Task VersionFourGranularHistorySurvivesGuardedEditsAndRegenerationInPlace()
    {
        const int list = 1000;
        const int scalar = 1001;
        var path = PathFor("legacy-writer");
        await using (var empty = new Store(path)) { }
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version=4";
        command.ExecuteNonQuery();

        var record = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var item = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2 };
        record.Values[list.ToString()] = JsonSerializer.SerializeToElement(new[] { item.Id, item.Id });
        record.Values[scalar.ToString()] = JsonSerializer.SerializeToElement(1);
        var types = ImmutableDictionary<int, int>.Empty.Add(list, 28).Add(scalar, 2);
        var emptyTypes = ImmutableDictionary<int, int>.Empty;
        var coverage = NativeImportCoverage.Capture([new(record.Id, 1, null, 0), new(item.Id, 2, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(1, types).Add(2, emptyTypes), "legacy import fixture");
        RecordCommit seed;
        await using (var writer = new Store(path))
        {
            await writer.ImportNativeAsync([new(record, types), new(item, emptyTypes)], Guid.NewGuid(), coverage);
            seed = Assert.Single(await writer.CommitsAsync());
            Assert.True(seed.ChangeCount > 3);
        }
        command.CommandText = """
            SELECT group_concat(hex(Change), ':') FROM
            (SELECT Change FROM ChangeEntities WHERE CommitId=$seed ORDER BY "Index")
            """;
        command.Parameters.AddWithValue("$seed", NetworkGuidConverter.Write(seed.Id));
        var originalChanges = command.ExecuteScalar();
        Assert.IsType<string>(originalChanges);

        var client = Guid.NewGuid();
        await using (var upgraded = new Store(path))
        {
            var revision = (await upgraded.ReferencesAsync()).Revision;
            await upgraded.SetFieldAsync(record.Id, scalar, JsonSerializer.SerializeToElement(1),
                JsonSerializer.SerializeToElement(2), revision, client);
            var prior = Assert.Single(await upgraded.ReadAsync(), r => r.Id == record.Id);
            await upgraded.CommitListOperationsAsync([ListAuthoring.Delete(prior, list, client, 1)], client);
            await upgraded.RegenerateAsync();
        }
        await using var reopened = new Store(path);
        var edited = Assert.Single(await reopened.ReadAsync(), r => r.Id == record.Id);
        Assert.Equal(2, edited.Values[scalar.ToString()].GetInt32());
        Assert.Equal([item.Id], reopened.Lists.Read(edited, list));
        Assert.Contains(seed, await reopened.CommitsAsync());
        Assert.Equal(originalChanges, command.ExecuteScalar());
        command.CommandText = "PRAGMA user_version";
        Assert.Equal(4L, command.ExecuteScalar());
        command.CommandText = "SELECT max(AuthoredModelVersion) FROM ChangeEntities";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task EarlierDevelopmentFormatsAreRefusedWithAClearMessage(int format)
    {
        var path = PathFor("format" + format);
        await using (var store = new Store(path))
            await store.ImportAsync([new(Record("value"), new Dictionary<int, int>())], Guid.NewGuid());
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA user_version={format}";
            command.ExecuteNonQuery();
        }
        var error = Assert.Throws<InvalidDataException>(() => new Store(path));
        Assert.Equal("Unsupported record store format.", error.Message);
    }

    [Fact]
    public void PageAccountingClosesItsSqliteBackupBeforeReadingBytes()
    {
        using var source = Open(PathFor("closed-backup"));
        using var command = source.CreateCommand();
        command.CommandText = "CREATE TABLE Payload(Data BLOB); INSERT INTO Payload VALUES(zeroblob(32));";
        command.ExecuteNonQuery();

        var tables = SqlitePageStatistics.Read(source, path =>
        {
            if (OperatingSystem.IsLinux())
            {
                var resolved = Store.ResolvePath(path);
                Assert.DoesNotContain(Directory.EnumerateFiles("/proc/self/fd"),
                    fd => new FileInfo(fd).LinkTarget == resolved);
            }
            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            var bytes = new byte[file.Length];
            file.ReadExactly(bytes);
            return bytes;
        });

        Assert.Contains(tables, table => table.Name == "Payload" && table.Payload >= 32);
    }

    [Fact]
    public void PageAccountingIncludesOverflowAndUncheckpointedWal()
    {
        var path = PathFor("pages");
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL; CREATE TABLE Payload(Id INTEGER PRIMARY KEY, Data BLOB);";
        command.ExecuteNonQuery();
        command.CommandText = "INSERT INTO Payload(Data) VALUES(zeroblob(100))";
        using (var transaction = connection.BeginTransaction())
        {
            command.Transaction = transaction;
            for (var i = 0; i < 500; i++) command.ExecuteNonQuery();
            transaction.Commit();
        }
        command.Transaction = null;
        command.CommandText = "INSERT INTO Payload(Data) VALUES(zeroblob(65536)); CREATE INDEX PayloadByData ON Payload(Data)";
        command.ExecuteNonQuery();
        var metrics = StorageMeasurement.Read(path);
        Assert.True(metrics.Wal > 0);
        Assert.Equal(new FileInfo(path + "-wal").Length, metrics.Wal);
        Assert.Equal(117541, metrics.Tables.Single(t => t.Name == "Payload").Payload);
        Assert.True(metrics.Tables.Sum(t => t.Bytes) > metrics.Main);
    }

    [Fact]
    public async Task OnTheFullPathAMissingSnapshotCannotProduceSuccessfulPartialReadsOrCompleteReferenceFacts()
    {
        var path = PathFor("snapshot-root");
        var value = Record(Guid.NewGuid().ToString());
        await using var store = new Store(path) { CertifiedAcquisitionAllowed = false };
        await store.ImportAsync([new(value, new Dictionary<int, int>())], Guid.NewGuid());
        using var connection = Open(path);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Snapshots";
        command.ExecuteNonQuery();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReferencesAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RebuildReferenceIndexAsync());
        await store.RegenerateAsync();
        Assert.Equal(value.Values["text"].GetString(), Assert.Single(await store.ReadAsync()).Values["text"].GetString());
    }

    [Fact]
    public async Task OnACertifiedPointDeletedSnapshotsDoNotStopAReferenceQueryButKeyedAndFullReadsRefuseAndVerifyNamesThem()
    {
        var path = PathFor("snapshot-certified");
        var value = Record(Guid.NewGuid().ToString());
        await using (var importer = new Store(path)) await importer.ImportAsync([new(value, new Dictionary<int, int>())], Guid.NewGuid());
        using (var connection = Open(path))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Snapshots";
            command.ExecuteNonQuery();
        }
        await using var store = new Store(path);
        // The headers, edges and chunk rows are intact and no unread chunk is consulted, so the query is answered.
        var index = await store.ReferencesAsync();
        Assert.Equal(1, store.CertifiedAcquisitions);
        Assert.NotEmpty(index.Revision);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadRecordAsync(value.Id));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync());
        var failure = await Assert.ThrowsAsync<InvalidDataException>(() => new Store(path).VerifyAsync());
        Assert.Contains("Snapshots", failure.Message);
    }

    [Theory]
    [InlineData(4, 0)]
    [InlineData(7, 1)]
    public async Task APublicationReadsTombstonesOnlyWhenItWritesDerivedTables(int format, int reads)
    {
        var path = PathFor("tombstone-skip-" + format);
        await using (var empty = new Store(path)) { }
        if (format == 4)
        {
            using var connection = Open(path);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version=4";
            command.ExecuteNonQuery();
        }
        var record = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var types = ImmutableDictionary<int, int>.Empty;
        var coverage = NativeImportCoverage.Capture([new(record.Id, 1, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(1, types), "tombstone fixture");
        await using var store = new Store(path);
        await store.ImportNativeAsync([new(record, types)], Guid.NewGuid(), coverage);
        Assert.Equal(reads, store.TombstoneReads);
    }

    private static GenericRecord Record(string value)
    {
        var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
        record.Values["text"] = JsonSerializer.SerializeToElement(value);
        return record;
    }
    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
