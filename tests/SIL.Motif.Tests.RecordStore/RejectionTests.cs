using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.RecordStore;
using Xunit;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

public sealed class RejectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "motif-record-reject-" + Guid.NewGuid().ToString("N"));
    private string PathFor(string name)
    {
        Directory.CreateDirectory(root);
        return Path.Combine(root, name + ".sqlite");
    }

    private static GenericRecord Sample(string kind = "test")
    {
        var record = new GenericRecord { Id = Guid.NewGuid(), Kind = kind };
        record.Values["10"] = JsonSerializer.SerializeToElement("text");
        record.Values["11"] = JsonSerializer.SerializeToElement(new { en = "gloss" });
        record.Values["12"] = JsonSerializer.SerializeToElement(new[] { Guid.NewGuid(), Guid.NewGuid() });
        return record;
    }

    private static readonly Dictionary<int, int> SampleTypes = new() { [10] = 15, [11] = 14, [12] = 27 };

    [Fact]
    public async Task UnknownFieldTypesAreRefusedBeforeAnyHistoryIsWritten()
    {
        // The valid group alone exceeds one commit, so a lazy layout check would already have written history.
        var good = new GenericRecord { Id = Guid.NewGuid() };
        var goodTypes = new Dictionary<int, int>();
        for (var field = 1; field <= 900; field++)
        {
            good.Values[(20000 + field).ToString()] = JsonSerializer.SerializeToElement(field);
            goodTypes[20000 + field] = 2;
        }
        var bad = Sample("other");
        await using var store = new Store(PathFor("unknown-type"));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(
            [new(good, goodTypes), new(bad, new Dictionary<int, int> { [10] = 99, [11] = 14, [12] = 27 })], Guid.NewGuid()));
        Assert.Equal("Unknown field type.", error.Message);
        Assert.Empty(await store.CommitsAsync());
    }

    [Fact]
    public async Task ConflictingFieldTypesWithinOneLayoutAreRefused()
    {
        await using var store = new Store(PathFor("conflict"));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => store.ImportAsync(
            [new(Sample(), SampleTypes), new(Sample(), new Dictionary<int, int> { [10] = 13, [11] = 14, [12] = 27 })], Guid.NewGuid()));
        Assert.Equal("Conflicting field types.", error.Message);
    }

    [Theory]
    [InlineData(2, null, "Unknown field type.")]
    public void LayoutsWithAnUnknownTableOrTypeAreRefusedWhenARecordIsBuilt(int version, string? table, string message)
    {
        var layout = JsonSerializer.SerializeToElement(new
        {
            version, table = table ?? BuiltInLayouts.Version, types = new Dictionary<string, int> { ["10"] = 99 },
            defaults = new Dictionary<string, int>()
        });
        var error = Assert.Throws<InvalidDataException>(() => RecordInitialization.Create(Guid.NewGuid(), "lcm", 1, null, 0, 0, 0, layout, null));
        Assert.Equal(message, error.Message);
    }

    // Changing this breaks every existing store; the release notes must tell users to export first.
    private const string PinnedBuiltInLayoutsVersion = "lcm-builtin/69c48830b739b691";

    [Fact]
    public void BuiltInLayoutsVersionIsPinnedSoStoresBreakOnlyOnPurpose() =>
        Assert.Equal(PinnedBuiltInLayoutsVersion, BuiltInLayouts.Version);

    [Fact]
    public void AStoreWithADifferentTableVersionIsRefusedWithExportThenReimportAdvice()
    {
        var layout = JsonSerializer.SerializeToElement(new
        {
            version = 2, table = "lcm-builtin/other", types = new Dictionary<string, int>(), defaults = new Dictionary<string, int>()
        });
        var error = Assert.Throws<InvalidDataException>(() => RecordInitialization.Create(Guid.NewGuid(), "lcm", 1, null, 0, 0, 0, layout, null));
        Assert.Contains("different FieldWorks data model", error.Message);
        Assert.Contains("lcm-builtin/other", error.Message);
        Assert.Contains(BuiltInLayouts.Version, error.Message);
        Assert.Contains("Export it with the Motif build that wrote it, then re-import", error.Message);
    }

    [Fact]
    public void EveryTruncationAndAnyTrailingByteOfASnapshotOrChangeIsRefused()
    {
        var content = new RecordContent();
        var codec = new RecordCodec(content);
        var record = RecordInitialization.Prepare(Sample(), RecordInitialization.Layout([new RecordImport(Sample(), SampleTypes)]));
        record.Values["12"] = JsonSerializer.SerializeToElement(new[] { Guid.NewGuid() });
        var snapshot = codec.Snapshot(record);
        var change = codec.Change(new InsertRecordItem(record.Id, 12, Guid.NewGuid(), 0));
        foreach (var bytes in new[] { snapshot, change })
        {
            var decode = bytes == snapshot ? (Action<byte[]>)(b => codec.Snapshot(b)) : b => codec.Change(b);
            decode(bytes);
            for (var length = 0; length < bytes.Length; length++)
            {
                var prefix = bytes[..length];
                Assert.Throws<InvalidDataException>(() => decode(prefix));
            }
            Assert.Throws<InvalidDataException>(() => decode([..bytes, 0]));
        }
    }

    [Fact]
    public void OversizeCompressedContentIsRefusedWhenDecodedNotWhenOpened()
    {
        var raw = new byte[65 * 1024 * 1024];
        raw[0] = 2;
        using var packed = new MemoryStream();
        packed.WriteByte(1);
        using (var brotli = new BrotliStream(packed, CompressionLevel.Fastest, true)) brotli.Write(raw);
        var content = new RecordContent();
        content.Load([new ContentRow { Id = 1, Data = packed.ToArray() }]);
        Assert.Equal("Record content is too large.", Assert.Throws<InvalidDataException>(() => content.Get(1)).Message);
    }

    [Fact]
    public void ContentNestedPastTheDepthLimitIsRefusedOnWriteAndOnRead()
    {
        byte[] nested = [0, 2, ..Enumerable.Repeat<byte>(8, 70).SelectMany(open => new[] { open, (byte)1 }), 0];
        var content = new RecordContent();
        content.Load([new ContentRow { Id = 1, Data = nested }]);
        Assert.Equal("Record content nesting is too deep.", Assert.Throws<InvalidDataException>(() => content.Get(1)).Message);
        using var document = JsonDocument.Parse(new string('[', 70) + new string(']', 70), new JsonDocumentOptions { MaxDepth = 128 });
        Assert.Equal("Record content nesting is too deep.",
            Assert.Throws<InvalidDataException>(() => new RecordContent().Put(document.RootElement)).Message);
    }

    [Theory]
    [InlineData("SnapshotDigest", "snapshot projection is corrupt")]
    [InlineData("ReplayDigest", "History is corrupt")]
    [InlineData("IndexDigest", "Incoming-reference index is corrupt")]
    public async Task EachPublishedSetDigestMismatchIsRefusedByItsOwnCheck(string column, string expected)
    {
        var path = PathFor("digest-" + column);
        var sample = Sample();
        await using (var created = new Store(path))
            await created.ImportAsync([new(sample, SampleTypes)], Guid.NewGuid());
        using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"UPDATE RecordStoreState SET {column}='{new string('0', 64)}'";
            Assert.Equal(1, command.ExecuteNonQuery());
        }
        await using var store = new Store(path);
        switch (column)
        {
            case "SnapshotDigest":
                Assert.Contains(expected, (await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync())).Message);
                Assert.Contains(expected, (await Assert.ThrowsAsync<InvalidDataException>(() => store.ReferencesAsync())).Message);
                break;
            case "ReplayDigest":
                Assert.Contains(expected, (await Assert.ThrowsAsync<InvalidDataException>(() => store.RegenerateAsync())).Message);
                Assert.Contains(expected, (await Assert.ThrowsAsync<InvalidDataException>(() => store.RebuildReferenceIndexAsync())).Message);
                break;
            default:
                var references = await store.ReferencesAsync();
                Assert.Contains(references.Incoming([sample.Id]).Limitations, limitation => limitation.Contains(expected));
                break;
        }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
