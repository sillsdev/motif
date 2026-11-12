using SIL.LCModel;
using SIL.Motif.Scripture;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Scripture;

[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class SourceListTests(PristineProjectFixture pristine)
{
    private static readonly SeededSourceList.Item Web = new("WEB", "World English Bible", "GEN-ROM; 1CO 13-15", "Paratext", "English");

    [Fact]
    public void ProjectWithoutTheListReadsAsEmpty()
    {
        using var cache = pristine.NewScratch();

        Assert.Empty(SourceList.Read(cache));
    }

    [Fact]
    public void ReadsNameKindContextAndVersificationInListOrder()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web, new SeededSourceList.Item("CEB", "Cebuano", "NT", null, "Original"));

        var sources = SourceList.Read(cache);

        Assert.Collection(sources,
            first =>
            {
                Assert.True(first.IsValid, first.Error);
                Assert.Equal("WEB", first.Id);
                Assert.Equal("World English Bible", first.Name);
                Assert.Equal(SourceKind.Paratext, first.Kind);
                Assert.Equal("GEN-ROM; 1CO 13-15", first.ContextText);
                Assert.Equal("English", first.VersificationName);
                Assert.True(first.Context!.Contains("1CO", 14));
                Assert.False(first.Context.Contains("1CO", 12));
            },
            second =>
            {
                Assert.True(second.IsValid, second.Error);
                Assert.Equal(SourceKind.Paratext, second.Kind);
                Assert.True(second.Context!.Contains("MAT", 1));
            });
    }

    [Fact]
    public void ListMadeByHandWithoutTheCustomFieldsReadsAsParatextButNeedsAVersification()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddListWithoutFields(cache, Web);

        var entry = Assert.Single(SourceList.Read(cache));

        Assert.Equal(SourceKind.Paratext, entry.Kind);
        Assert.Contains("has no versification", entry.Error);
    }

    [Fact]
    public void MalformedContextComesBackAsAnErrorNotAnException()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web, new SeededSourceList.Item("BAD", "Bad", "1COR 3", "Paratext", "English"));

        var sources = SourceList.Read(cache);

        Assert.True(sources[0].IsValid);
        Assert.False(sources[1].IsValid);
        Assert.Null(sources[1].Context);
        Assert.Contains("1COR", sources[1].Error);
    }

    [Theory]
    [InlineData("Paratext", "", "has no versification")]
    [InlineData("Paratext", "Klingon", "not known")]
    [InlineData("Alien", "English", "kind 'Alien'")]
    [InlineData("Corpus", "English", "not supported yet")]
    public void UnusableItemsAreReportedWithAReason(string kind, string versification, string fragment)
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, new SeededSourceList.Item("X", "X", "NT", kind, versification));

        var entry = Assert.Single(SourceList.Read(cache));

        Assert.False(entry.IsValid);
        Assert.Contains(fragment, entry.Error);
    }

    [Fact]
    public void CorpusKindIsRecordedEvenThoughItIsNotBuilt()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, new SeededSourceList.Item("NOTES", "Notes", "*.txt", "Corpus", "English"));

        Assert.Equal(SourceKind.Corpus, Assert.Single(SourceList.Read(cache)).Kind);
    }

    [Fact]
    public void ItemWithNoIdIsUnusableAndRepeatedIdsAreBothUnusable()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, new SeededSourceList.Item("", "Nameless", "NT", "Paratext", "English"), Web with { Name = "One" },
            Web with { Name = "Two", Id = "web" });

        var sources = SourceList.Read(cache);

        Assert.Contains("no id", sources[0].Error);
        Assert.Contains("more than one", sources[1].Error);
        Assert.Contains("more than one", sources[2].Error);
    }

    [Fact]
    public void TwoListsWithTheNameAreRefused()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web);
        SeededSourceList.AddList(cache, Web);

        var refusal = Assert.Throws<SourceListAmbiguousException>(() => SourceList.Read(cache));

        Assert.Contains("2 possibility lists", refusal.Message);
    }
}
