using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using SIL.Motif.RecordStore.Lcm;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class StoreCoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-core-" + Guid.NewGuid().ToString("N"));
    private string Database
    {
        get { Directory.CreateDirectory(root); return Path.Combine(root, "records.sqlite"); }
    }

    [Fact]
    public async Task LargeCreateIsSplitAtFiveHundredChangesAndReplayIsEqual()
    {
        var record = new GenericRecord { Id = Guid.NewGuid() };
        var items = Enumerable.Range(0, 2528).Select(_ => Guid.NewGuid()).ToArray();
        record.Values["1000"] = JsonSerializer.SerializeToElement(items);
        var types = new Dictionary<int, int> { [1000] = 27 };
        await using (var store = new Store(Database))
        {
            await store.ImportAsync([new(record, types)], Guid.NewGuid());
            var commits = await store.CommitsAsync();
            Assert.Equal(6, commits.Count);
            Assert.Equal(2529, commits.Sum(c => c.ChangeCount));
            Assert.All(commits, c => Assert.InRange(c.ChangeCount, 1, 500));
            Assert.Equal(items, store.Lists.Read(Assert.Single(await store.ReadAsync()), 1000));
            await store.RegenerateAsync();
            Assert.Equal(items, store.Lists.Read(Assert.Single(await store.ReadAsync()), 1000));
        }
        await using var reopened = new Store(Database);
        Assert.Equal(items, reopened.Lists.Read(Assert.Single(await reopened.ReadAsync()), 1000));
    }

    [Fact]
    public async Task CommitTimesAreWholeMicrosecondsUtcAndRemainOrderedAcrossReopen()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(1)).AddTicks(19);
        var clock = new FixedClock(now);
        var record = new GenericRecord { Id = Guid.NewGuid() };
        for (var i = 0; i < 1001; i++) record.Values[(i + 10000).ToString()] = JsonSerializer.SerializeToElement(i + 1);
        IReadOnlyList<RecordCommit> written;
        await using (var store = new Store(Database, clock))
        {
            await store.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
            written = await store.CommitsAsync();
            Assert.Equal(3, written.Count);
            Assert.All(written, c =>
            {
                Assert.Equal(TimeSpan.Zero, c.UtcTime.Offset);
                Assert.Equal(now.UtcTicks - now.UtcTicks % 10, c.UtcTime.Ticks);
            });
            Assert.Equal(new long[] { 0, 1, 2 }, written.Select(c => c.Counter).Order());
        }
        await using var reopened = new Store(Database, clock);
        var loaded = await reopened.CommitsAsync();
        Assert.Equal(written.OrderBy(c => c.Id), loaded.OrderBy(c => c.Id));
    }

    [Fact]
    public async Task NativeBaselineRollsBackWhenPublicationFailsAndCanBeRetried()
    {
        var path = Database;
        var record = new GenericRecord { Id = Guid.NewGuid() };
        var target = Guid.NewGuid();
        var initial = Enumerable.Repeat(target, 2501).ToArray();
        record.Values["1000"] = JsonSerializer.SerializeToElement(initial);
        var types = ImmutableDictionary<int, int>.Empty.Add(1000, 28);
        var coverage = NativeImportCoverage.Capture([new(record.Id, record.ClassId, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(record.ClassId, types), "native import fixture");
        await using (var store = new Store(path))
        {
            using var connection = new SqliteConnection(RecordSqlite.ConnectionString(path));
            connection.Open();
            using var trigger = connection.CreateCommand();
            trigger.CommandText = """
                CREATE TRIGGER refuse_index BEFORE INSERT ON IncomingReferences
                BEGIN SELECT RAISE(ABORT, 'refuse publication'); END
                """;
            trigger.ExecuteNonQuery();
            await Assert.ThrowsAsync<SqliteException>(() => store.ImportNativeAsync([new(record, types)], Guid.NewGuid(), coverage));
            Assert.Empty(await store.CommitsAsync());
            trigger.CommandText = "DROP TRIGGER refuse_index";
            trigger.ExecuteNonQuery();
        }
        await using var reopened = new Store(path);
        await reopened.ImportNativeAsync([new(record, types)], Guid.NewGuid(), coverage);
        Assert.Equal(2, Assert.Single(await reopened.CommitsAsync()).ChangeCount);
        await reopened.RegenerateAsync();
        var restored = Assert.Single(await reopened.ReadAsync(), r => r.Id == record.Id);
        Assert.Equal(initial, reopened.Lists.Read(restored, 1000));
    }

    [Fact]
    public async Task PackedImportRefusesAMissingVectorBeforePublishing()
    {
        var first = new GenericRecord { Id = Guid.NewGuid() };
        var second = new GenericRecord { Id = Guid.NewGuid() };
        first.Values["1000"] = JsonSerializer.SerializeToElement(Array.Empty<Guid>());
        var types = ImmutableDictionary<int, int>.Empty.Add(1000, 28);
        var coverage = NativeImportCoverage.Capture([new(first.Id, 0, null, 0), new(second.Id, 0, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(0, types), "native import fixture");
        await using var store = new Store(Database);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportNativeAsync(
            [new(first, types), new(second, types)], Guid.NewGuid(), coverage));
        Assert.Empty(await store.CommitsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(store.ReadAsync);
        second.Values["1000"] = first.Values["1000"];
        await store.ImportNativeAsync([new(first, types), new(second, types)], Guid.NewGuid(), coverage);
        Assert.Equal(3, (await store.ReadAsync()).Count);
    }

    [Fact]
    public async Task GranularImportRollsBackWhenPublicationFailsAndCanBeRetried()
    {
        var path = Database;
        var target = new GenericRecord { Id = Guid.NewGuid() };
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values["1000"] = JsonSerializer.SerializeToElement(Enumerable.Repeat(target.Id, 501));
        var types = ImmutableDictionary<int, int>.Empty.Add(1000, 28);
        var coverage = NativeImportCoverage.Capture([new(record.Id, 0, null, 0), new(target.Id, 0, null, 0)],
            ImmutableDictionary<int, ImmutableDictionary<int, int>>.Empty.Add(0, types), "native import fixture");
        var records = new RecordImport[] { new(record, types), new(target, types) };
        await using (var empty = new Store(path)) { }
        using var connection = new SqliteConnection(RecordSqlite.ConnectionString(path));
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TRIGGER refuse_index BEFORE INSERT ON IncomingReferences
            BEGIN SELECT RAISE(ABORT, 'refuse publication'); END
            """;
        command.ExecuteNonQuery();

        Task Import(Store store) => store.ImportAsync(records, Guid.NewGuid(), coverage);

        await using (var store = new Store(path))
        {
            await Assert.ThrowsAsync<SqliteException>(() => Import(store));
            Assert.Empty(await store.CommitsAsync());
        }
        command.CommandText = "DROP TRIGGER refuse_index";
        command.ExecuteNonQuery();
        await using var reopened = new Store(path);
        await Import(reopened);
        Assert.Equal(2, (await reopened.CommitsAsync()).Count);
        Assert.Equal(501, reopened.Lists.Read(Assert.Single(await reopened.ReadAsync(), r => r.Id == record.Id), 1000).Count);
    }

    [Fact]
    public async Task ReadValuesAndCloneAreIsolatedAndRepeatedImportIsRefused()
    {
        var record = new GenericRecord { Id = Guid.NewGuid() };
        record.Values["name"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString());
        await using var store = new Store(Database);
        await store.ImportAsync([new(record, new Dictionary<int, int>())], Guid.NewGuid());
        var original = LcmGraphReader.Canonical(Assert.Single(await store.ReadAsync()));
        Assert.Single(await store.ReadAsync()).Values.Clear();
        Assert.Equal(original, LcmGraphReader.Canonical(Assert.Single(await store.ReadAsync())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAsync([], Guid.NewGuid()));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("/absolute")]
    [InlineData("C:/absolute")]
    [InlineData("WritingSystemStore\\escape.ldml")]
    [InlineData("WritingSystemStore//escape.ldml")]
    public void ExportRefusesUnsafeResourcePaths(string relative) =>
        Assert.Throws<InvalidDataException>(() => LcmRecordBridge.ResourcePath(root, relative));

    [Fact]
    public void CanonicalGateDetectsChangedScalarAndMissingObject()
    {
        var original = new GenericRecord { Id = Guid.NewGuid() };
        original.Values["name"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString());
        var schema = JsonSerializer.SerializeToElement(new { fields = Array.Empty<object>() });
        var changed = (GenericRecord)original.Copy();
        changed.Values["name"] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString());
        Assert.NotEmpty(GraphComparison.Differences([original], [changed], schema));
        Assert.NotEmpty(GraphComparison.Differences([original], [], schema));
        var clone = (GenericRecord)original.Copy();
        Assert.Empty(GraphComparison.Differences([original], [clone], schema));
    }

    [Fact]
    public void LinkedFilesExemptionDoesNotHideClearedOrMissingFields()
    {
        var original = new GenericRecord { Id = Guid.NewGuid(), ClassId = 6001 };
        var sourceRoot = Path.Combine(root, "before");
        var destinationRoot = Path.Combine(root, "after");
        original.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(sourceRoot, "LinkedFiles")));
        var schema = JsonSerializer.SerializeToElement(new { fields = new[] { new { id = 6001042, name = "LinkedFilesRootDir", owner = "LangProject", type = 15 } } });
        var changed = (GenericRecord)original.Copy();
        changed.Values.Remove("6001042");
        Assert.NotEmpty(GraphComparison.Differences([original], [changed], schema, sourceRoot, destinationRoot));
        changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(""));
        Assert.NotEmpty(GraphComparison.Differences([original], [changed], schema, sourceRoot, destinationRoot));
        changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(destinationRoot, "LinkedFiles")));
        Assert.Empty(GraphComparison.Differences([original], [changed], schema, sourceRoot, destinationRoot));
    }

    [Fact]
    public void StreamedCanonicalGateAcceptsAnExactOrProjectRelativeLinkedFilesRoot()
    {
        Directory.CreateDirectory(root);
        var canonical = Path.Combine(root, "source.jsonl");
        var sourceRoot = Path.Combine(root, "before");
        var destinationRoot = Path.Combine(root, "after");
        var original = new GenericRecord { Id = Guid.NewGuid(), ClassId = 6001 };
        original.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(sourceRoot, "LinkedFiles")));
        var schema = JsonSerializer.SerializeToElement(new { fields = new[] { new { id = 6001042, name = "LinkedFilesRootDir", owner = "LangProject", type = 15 } } });
        GraphComparison.WriteCanonical(canonical, [original]);

        GraphComparison.AssertCanonical(canonical, [original], schema, sourceRoot, destinationRoot);
        var changed = (GenericRecord)original.Copy();
        changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(destinationRoot, "LinkedFiles")));
        GraphComparison.AssertCanonical(canonical, [changed], schema, sourceRoot, destinationRoot);
        changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(destinationRoot, "Unrelated")));
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            GraphComparison.AssertCanonical(canonical, [changed], schema, sourceRoot, destinationRoot));
    }

    [Fact]
    public void StreamedCanonicalGateAcceptsOnlyTheExactImportedLinkedFilesRoot()
    {
        Directory.CreateDirectory(root);
        var canonical = Path.Combine(root, "imported.jsonl");
        var sourceRoot = Path.Combine(root, "verify-source");
        var destinationRoot = Path.Combine(root, "restored");
        var importedRoot = Path.Combine(root, "import-source");
        var original = new GenericRecord { Id = Guid.NewGuid(), ClassId = 6001 };
        original.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(sourceRoot, "LinkedFiles")));
        var schema = JsonSerializer.SerializeToElement(new { fields = new[] { new { id = 6001042, name = "LinkedFilesRootDir", owner = "LangProject", type = 15 } } });
        GraphComparison.WriteCanonical(canonical, [original]);

        var changed = (GenericRecord)original.Copy();
        changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(Path.Combine(importedRoot, "LinkedFiles")));
        GraphComparison.AssertCanonical(canonical, [changed], schema, sourceRoot, destinationRoot, importedRoot);
        Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
            GraphComparison.AssertCanonical(canonical, [changed], schema, sourceRoot, destinationRoot));
        foreach (var rejected in new[]
        {
            Path.Combine(root, "other-source", "LinkedFiles"),
            Path.Combine(importedRoot, "Nested", "LinkedFiles"),
            Path.Combine(importedRoot, "LinkedFiles", "Sub"),
            importedRoot
        })
        {
            changed.Values["6001042"] = JsonSerializer.SerializeToElement(RawPath(rejected));
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() =>
                GraphComparison.AssertCanonical(canonical, [changed], schema, sourceRoot, destinationRoot, importedRoot));
        }
    }

    private static object RawPath(string path) => new
    {
        text = path, utf16le = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(path))
    };

    [Fact]
    public void ResourcesAreDeduplicatedOutsideTheLogAndCorruptionIsRefused()
    {
        var database = Database;
        var content = new ResourceContentStore(database);
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);
        var hash = content.Put(bytes);
        Assert.Equal(hash, content.Put(bytes));
        Assert.Equal(bytes, content.Read(hash));
        Assert.Single(Directory.GetFiles(database + ".resources"));
        File.WriteAllBytes(Path.Combine(database + ".resources", hash), []);
        Assert.Throws<InvalidDataException>(() => content.Read(hash));
        Assert.Throws<InvalidDataException>(() => content.Read("../escape"));
    }

    private sealed class FixedClock(DateTimeOffset time) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => time;
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
