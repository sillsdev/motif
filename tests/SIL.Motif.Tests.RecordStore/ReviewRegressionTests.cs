using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class ReviewRegressionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-review-" + Guid.NewGuid().ToString("N"));
    private string Database { get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); } }

    [Fact]
    public async Task AHandleOpenedBeforePublicationExplicitlyRequiresReopen()
    {
        await using (var writer = new Store(Database))
        await using (var reader = new Store(Database))
        {
            var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
            record.Values["value"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
            await writer.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => reader.ReadAsync());
            Assert.Contains("reopen", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        await using var reopened = new Store(Database);
        Assert.Single(await reopened.ReadAsync());
    }

    [Fact]
    public async Task CorruptHistoryCannotDestroyTheLastReadableProjection()
    {
        var record = new GenericRecord { Id = Guid.NewGuid(), Kind = "test" };
        record.Values["value"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
        await using var store = new Store(Database);
        await store.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Database, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ChangeEntities WHERE substr(Change,1,2) = x'0102'";
        Assert.Equal(1, command.ExecuteNonQuery());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RebuildReferenceIndexAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => store.RegenerateAsync());
        Assert.Equal(record.Values["value"].GetGuid(), Assert.Single(await store.ReadAsync()).Values["value"].GetGuid());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeCountsRequireCompleteSourceCoverage(bool suppliedButMissingField)
    {
        var target = new GenericRecord { Id = Guid.NewGuid(), ClassId = 1 };
        var source = new GenericRecord { Id = Guid.NewGuid(), ClassId = 2 };
        var fields = ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty
            .Add(1, ImmutableDictionary<int, int>.Empty)
            .Add(2, ImmutableDictionary<int, int>.Empty.Add(20, 24));
        var coverage = NativeImportCoverage.Capture(new[] { target, source }.Select(r =>
            new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)), fields, "complete-runtime-fixture");
        await using var store = new Store(Database);
        await store.ImportAsync([new(target, new Dictionary<int, int>()), new(source, new Dictionary<int, int> { [20] = 24 })],
            Guid.NewGuid(), suppliedButMissingField ? coverage : null);
        var initial = await store.ReferencesAsync();
        var evidence = new ReferenceEvidence(initial.Revision, "empty", "empty", "empty", []);
        var result = (await store.ReferencesAsync(evidence)).Incoming([target.Id]);
        Assert.False(result.NativeComplete);
        Assert.Null(result.NativeTotal);
        Assert.Null(result.Total);
        Assert.Throws<InvalidOperationException>(result.RequireComplete);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
