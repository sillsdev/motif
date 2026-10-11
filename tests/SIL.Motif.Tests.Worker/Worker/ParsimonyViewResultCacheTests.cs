using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class ParsimonyViewResultCacheTests
{
    private const int Entries = 4;
    private const int Rows = 10;

    private readonly ParsimonyViewResultCache _cache = new(Entries, Rows);

    private sealed record TestRow : ParsimonyViewRow;

    private static ParsimonyViewCacheKey Key(string name, string proof = "proof") =>
        new(proof, name, 1, "filters", null);

    private static ParsimonyViewResult Result(int rows) => new(
        Enumerable.Range(0, rows).Select(_ => (ParsimonyViewRow)new TestRow()).ToArray(), "digest", null, null, null);

    [Fact]
    public void AStoredResultIsFoundByItsKeyAndMissedByAnyChangedPart()
    {
        _cache.Store(Key("one"), Result(2));

        Assert.NotNull(_cache.Find(Key("one")));
        Assert.Equal(1, _cache.Hits);
        Assert.Null(_cache.Find(Key("two")));
        Assert.Null(_cache.Find(Key("one", proof: "changed-file")));
        Assert.Null(_cache.Find(Key("one") with { FilterDigest = "other" }));
        Assert.Null(_cache.Find(Key("one") with { Version = 2 }));
        Assert.Null(_cache.Find(Key("one") with { ReportId = "report" }));
        Assert.Equal(1, _cache.Hits);
    }

    [Fact]
    public void TheLeastRecentlyUsedEntryIsEvictedPastTheEntryBound()
    {
        for (var index = 0; index < Entries; index++) _cache.Store(Key("view-" + index), Result(1));
        Assert.NotNull(_cache.Find(Key("view-0")));

        _cache.Store(Key("overflow"), Result(1));

        Assert.Equal(Entries, _cache.Keys().Count);
        Assert.NotNull(_cache.Find(Key("view-0")));
        Assert.Null(_cache.Find(Key("view-1")));
    }

    [Fact]
    public void ResultsAreEvictedPastTheTotalRowBoundAndAnOversizedOneIsNotKept()
    {
        _cache.Store(Key("a"), Result(Rows / 2 + 1));
        _cache.Store(Key("b"), Result(Rows / 2 + 1));

        Assert.Null(_cache.Find(Key("a")));
        Assert.NotNull(_cache.Find(Key("b")));

        _cache.Store(Key("huge"), Result(Rows + 1));
        Assert.Null(_cache.Find(Key("huge")));
        Assert.NotNull(_cache.Find(Key("b")));
    }

    [Fact]
    public void StoringTheSameKeyAgainReplacesItAndKeepsTheRowCountExact()
    {
        _cache.Store(Key("a"), Result(Rows));
        _cache.Store(Key("a"), Result(Rows));

        Assert.Single(_cache.Keys());
        Assert.NotNull(_cache.Find(Key("a")));
    }
}
