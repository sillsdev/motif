using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.RecordStore;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

public sealed class LazyContentTests
{
    [Fact]
    public void LoadingHashesAndDeduplicatingDoNotDecodeAndStoredHashesKeepIdentityStable()
    {
        var small = JsonSerializer.SerializeToElement(new { tag = "small", n = 1 });
        var large = JsonSerializer.SerializeToElement(new { text = string.Concat(Enumerable.Repeat("compressible ", 200)) });
        var source = new RecordContent();
        var smallId = source.Put(small);
        var largeId = source.Put(large);
        var rows = source.Pending.Select(r => new ContentRow { Id = r.Id, Data = r.Data.ToArray() }).ToArray();
        Assert.Equal(0, rows[0].Data[0]);
        Assert.Equal(1, rows[1].Data[0]);
        var loaded = new RecordContent();
        loaded.Load(rows);
        Assert.Equal(0, loaded.DecodedCount);
        Assert.Equal(source.Digest(), loaded.Digest());
        Assert.Equal(smallId, loaded.Put(small));
        Assert.Equal(largeId, loaded.Put(large));
        Assert.Empty(loaded.Pending);
        Assert.Equal(0, loaded.DecodedCount);
        Assert.True(JsonElement.DeepEquals(large, loaded.Get(largeId)));
        Assert.True(JsonElement.DeepEquals(large, loaded.Get(largeId)));
        Assert.Equal(1, loaded.DecodedCount);
        var added = loaded.Put(JsonSerializer.SerializeToElement("new"));
        Assert.Equal(largeId + 1, added);
        Assert.NotEqual(source.Digest(), loaded.Digest());
    }

    [Fact]
    public void CorruptRowsAreStillRefusedWithoutDecodingContent()
    {
        var source = new RecordContent();
        source.Put(JsonSerializer.SerializeToElement(new { tag = "x" }));
        var row = source.Pending.Single();
        var future = new ContentRow { Id = row.Id, Data = [0, 3, ..row.Data[2..]] };
        Assert.Equal("Unknown content version.", Assert.Throws<InvalidDataException>(() => new RecordContent().Load([future])).Message);
        var envelope = new ContentRow { Id = row.Id, Data = [2, ..row.Data[1..]] };
        Assert.Equal("Unknown content envelope.", Assert.Throws<InvalidDataException>(() => new RecordContent().Load([envelope])).Message);
        var twin = new ContentRow { Id = row.Id + 1, Data = row.Data.ToArray() };
        var duplicate = new RecordContent();
        duplicate.Load([row, twin]);
        Assert.Throws<InvalidDataException>(() => duplicate.Put(JsonSerializer.SerializeToElement(1)));
    }
}
