using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PrefersDefaultAnalysisTextAndFallsBackToAnotherAlternative(bool hasDefaultText)
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web);
        cache.ServiceLocator.WritingSystemManager.GetOrSet("fr", out var french);
        var item = cache.LangProject.CheckListsOC
            .Single(list => list.Name.get_String(cache.DefaultAnalWs).Text == SourceListFormat.ListName)
            .PossibilitiesOS.Single();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            cache.ServiceLocator.WritingSystems.CurrentAnalysisWritingSystems.Insert(0, french);
            if (!hasDefaultText) return;
            item.Name.set_String(french.Handle, "Bible anglaise");
            item.Abbreviation.set_String(french.Handle, "BIB");
            item.Description.set_String(french.Handle, TsStringUtils.MakeString("MAT-JHN", french.Handle));
        });
        Assert.Equal(french.Handle, cache.DefaultAnalWs);
        Assert.NotEqual(cache.DefaultAnalWs, item.Name.AvailableWritingSystemIds.First());

        var entry = Assert.Single(SourceList.Read(cache));

        Assert.True(entry.IsValid, entry.Error);
        Assert.Equal(hasDefaultText ? "Bible anglaise" : Web.Name, entry.Name);
        Assert.Equal(hasDefaultText ? "BIB" : Web.Id, entry.Id);
        Assert.Equal(hasDefaultText ? "MAT-JHN" : Web.Context, entry.ContextText);
    }

    [Fact]
    public void FallsBackToAnotherAlternativeWhenDefaultAnalysisWritingSystemIsZero()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web with { Status = "Active" });
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.WritingSystems.CurrentAnalysisWritingSystems.Clear());
        Assert.Equal(0, cache.DefaultAnalWs);

        var entry = Assert.Single(SourceList.Read(cache));

        Assert.True(entry.IsValid, entry.Error);
        Assert.Equal(Web.Name, entry.Name);
        Assert.Equal(Web.Id, entry.Id);
        Assert.Equal(Web.Context, entry.ContextText);
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

    [Fact]
    public void ItemWithNoStatusIsActive()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web, Web with { Id = "BSB", Status = "" });

        Assert.All(SourceList.Read(cache), entry =>
        {
            Assert.True(entry.IsValid, entry.Error);
            Assert.Equal(SourceStatus.Active, entry.Status);
        });
    }

    [Fact]
    public void ListMadeByHandWithoutTheStatusFieldReadsAsActive()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddListWithoutFields(cache, Web);

        Assert.Equal(SourceStatus.Active, Assert.Single(SourceList.Read(cache)).Status);
    }

    [Fact]
    public void RemovedSourceIsReturnedMarkedWithItsIdAndContext()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web with { Status = "Removed" }, new SeededSourceList.Item("CEB", "Cebuano", "NT", "Paratext", "Original", "Active"));

        var sources = SourceList.Read(cache);

        Assert.Equal(2, sources.Count);
        Assert.Equal("WEB", sources[0].Id);
        Assert.Equal(SourceStatus.Removed, sources[0].Status);
        Assert.True(sources[0].IsValid, sources[0].Error);
        Assert.Equal("GEN-ROM; 1CO 13-15", sources[0].ContextText);
        Assert.Equal(SourceStatus.Active, sources[1].Status);
    }

    [Fact]
    public void StatusIsMatchedIgnoringCase()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web with { Status = "removed" });

        Assert.Equal(SourceStatus.Removed, Assert.Single(SourceList.Read(cache)).Status);
    }

    [Fact]
    public void UnknownStatusIsAnErrorEntryAndTheOthersStillRead()
    {
        using var cache = pristine.NewScratch();
        SeededSourceList.AddList(cache, Web with { Status = "Archived" }, new SeededSourceList.Item("CEB", "Cebuano", "NT", "Paratext", "Original"));

        var sources = SourceList.Read(cache);

        Assert.False(sources[0].IsValid);
        Assert.Contains("'Archived'", sources[0].Error);
        Assert.Contains("Active or Removed", sources[0].Error);
        Assert.True(sources[1].IsValid, sources[1].Error);
    }

    [Fact]
    public void AmbiguityRefusalCodeIsDotted() => Assert.Equal("source.list-ambiguous", SourceListAmbiguousException.Code);
}
