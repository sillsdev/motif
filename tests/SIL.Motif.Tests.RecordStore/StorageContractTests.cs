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
            command.CommandText = "PRAGMA user_version=3";
            command.ExecuteNonQuery();
            command.CommandText = "UPDATE ChangeEntities SET Change = x'ff'";
            command.ExecuteNonQuery();
        }
        await using var reopened = new Store(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.RegenerateAsync());
    }

    [Fact]
    public async Task FormatTwoStoresAreRefusedWithAClearMessage()
    {
        var path = PathFor("format2");
        await using (var store = new Store(path))
            await store.ImportAsync([new(Record("value"), new Dictionary<int, int>())], Guid.NewGuid());
        using (var connection = Open(path))
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=2";
            command.ExecuteNonQuery();
        }
        var error = Assert.Throws<InvalidDataException>(() => new Store(path));
        Assert.Equal("Unsupported record store format.", error.Message);
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
    public async Task MissingSnapshotCannotProduceSuccessfulPartialReadsOrCompleteReferenceFacts()
    {
        var path = PathFor("snapshot-root");
        var value = Record(Guid.NewGuid().ToString());
        await using var store = new Store(path);
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
