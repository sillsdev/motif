using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Texts;

/// <summary>Stems not tagged with an inflection class, measured the way the parser gives a stem its class.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class UntaggedStemReaderTests(PristineProjectFixture pristine)
{
    private IPartOfSpeech Category(LcmCache cache) =>
        cache.ServiceLocator.GetInstance<IPartOfSpeechRepository>().GetObject(pristine.Seed.PartOfSpeechId);

    [Fact]
    public void WithNoAffixRestrictedByClassNoStemIsCounted()
    {
        using var cache = pristine.NewScratch();
        AddSuffix(cache, Category(cache), "ba", restrictedTo: null);

        Assert.Equal(0, UntaggedStemReader.Read(cache).Count);
    }

    [Fact]
    public void AStemWithNoClassAndNoDefaultIsCountedWhenAnAffixOfItsCategoryIsRestrictedByClass()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        var inflectionClass = AddClass(cache, category);
        AddSuffix(cache, category, "ba", restrictedTo: inflectionClass);

        var untagged = UntaggedStemReader.Read(cache);

        Assert.Equal(2, untagged.Count);
        Assert.All(untagged.Top, stem => Assert.Equal(1, stem.RestrictedAffixes));
        Assert.Equal([SeededProject.FirstForm, SeededProject.SecondForm], untagged.Top.Select(stem => stem.Headword).Order());
        Assert.All(untagged.Top, stem => Assert.False(string.IsNullOrEmpty(stem.Category)));
    }

    [Fact]
    public void AStemTaggedWithAClassIsNotCounted()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        var inflectionClass = AddClass(cache, category);
        AddSuffix(cache, category, "ba", restrictedTo: inflectionClass);
        var first = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(pristine.Seed.FirstEntryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            first.MorphoSyntaxAnalysesOC.OfType<IMoStemMsa>().Single().InflectionClassRA = inflectionClass);

        var untagged = UntaggedStemReader.Read(cache);

        Assert.Equal(1, untagged.Count);
        Assert.Equal(SeededProject.SecondForm, Assert.Single(untagged.Top).Headword);
    }

    [Fact]
    public void ADefaultClassOnTheCategoryMeansNoStemIsCounted()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        var inflectionClass = AddClass(cache, category);
        AddSuffix(cache, category, "ba", restrictedTo: inflectionClass);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => category.DefaultInflectionClassRA = inflectionClass);

        Assert.Equal(0, UntaggedStemReader.Read(cache).Count);
    }

    [Fact]
    public void ASubcategoryStemTakesItsDefaultClassAndItsRestrictedAffixesFromTheCategoriesAbove()
    {
        using var cache = pristine.NewScratch();
        var parent = Category(cache);
        var inflectionClass = AddClass(cache, parent);
        AddSuffix(cache, parent, "ba", restrictedTo: inflectionClass);
        var child = AddSubcategory(cache, parent, "Sub");
        AddStem(cache, child, "kid");
        var seededOnly = UntaggedStemReader.Read(cache).Count;

        Assert.Equal(3, seededOnly);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => parent.DefaultInflectionClassRA = inflectionClass);
        Assert.Equal(0, UntaggedStemReader.Read(cache).Count);
    }

    [Fact]
    public void AnAffixOnlyOfASubcategoryDoesNotRestrictStemsOfTheCategoryAbove()
    {
        using var cache = pristine.NewScratch();
        var parent = Category(cache);
        var child = AddSubcategory(cache, parent, "Sub");
        var inflectionClass = AddClass(cache, child);
        AddSuffix(cache, child, "ba", restrictedTo: inflectionClass);

        Assert.Equal(0, UntaggedStemReader.Read(cache).Count);
    }

    [Fact]
    public void TheListIsBoundedAndRanksTheStemsWithTheMostHeldBackAffixesFirst()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        var inflectionClass = AddClass(cache, category);
        AddSuffix(cache, category, "ba", restrictedTo: inflectionClass);
        var other = AddSubcategory(cache, category, "Other");
        for (var index = 0; index < 12; index++) AddStem(cache, other, $"o{index:D2}");
        AddSuffix(cache, other, "bo", restrictedTo: inflectionClass);

        var untagged = UntaggedStemReader.Read(cache);

        Assert.Equal(14, untagged.Count);
        Assert.Equal(OverviewMeasureLimits.TopWords, untagged.Top.Count);
        Assert.All(untagged.Top, stem => Assert.Equal(2, stem.RestrictedAffixes));
        Assert.StartsWith("o", untagged.Top[0].Headword, StringComparison.Ordinal);
    }

    [Fact]
    public void StemsTiedAtTheCutAreKeptByHeadwordNotByTheOrderTheyWereAdded()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        var inflectionClass = AddClass(cache, category);
        AddSuffix(cache, category, "ba", restrictedTo: inflectionClass);
        var other = AddSubcategory(cache, category, "Other");
        for (var index = 11; index >= 0; index--) AddStem(cache, other, $"o{index:D2}");
        AddSuffix(cache, other, "bo", restrictedTo: inflectionClass);

        var untagged = UntaggedStemReader.Read(cache);

        Assert.Equal(Enumerable.Range(0, OverviewMeasureLimits.TopWords).Select(index => $"o{index:D2}"),
            untagged.Top.Select(stem => stem.Headword));
    }

    [Fact]
    public void TheSummaryCarriesTheMeasure()
    {
        using var cache = pristine.NewScratch();
        var category = Category(cache);
        AddSuffix(cache, category, "ba", restrictedTo: AddClass(cache, category));

        var summary = ProjectSummaryReader.Read(cache);

        Assert.Equal(2, summary.StemsWithoutInflectionClass.Count);
    }

    private static IMoInflClass AddClass(LcmCache cache, IPartOfSpeech category)
    {
        IMoInflClass inflectionClass = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            inflectionClass = cache.ServiceLocator.GetInstance<IMoInflClassFactory>().Create();
            category.InflectionClassesOC.Add(inflectionClass);
            inflectionClass.Name.set_String(cache.DefaultAnalWs, "Class A");
        });
        return inflectionClass;
    }

    private static IPartOfSpeech AddSubcategory(LcmCache cache, IPartOfSpeech parent, string name)
    {
        IPartOfSpeech child = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            child = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            parent.SubPossibilitiesOS.Add(child);
            child.Name.set_String(cache.DefaultAnalWs, name);
        });
        return child;
    }

    private static void AddStem(LcmCache cache, IPartOfSpeech category, string form)
    {
        var stemType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(stemType,
                TsStringUtils.MakeString(form, cache.DefaultVernWs), "gloss",
                SandboxGenericMSA.Create(MsaType.kStem, category)));
    }

    private static void AddSuffix(LcmCache cache, IPartOfSpeech category, string form, IMoInflClass? restrictedTo)
    {
        var suffixType = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphSuffix);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var entry = cache.ServiceLocator.GetInstance<ILexEntryFactory>().Create(suffixType,
                TsStringUtils.MakeString(form, cache.DefaultVernWs), "gloss",
                SandboxGenericMSA.Create(MsaType.kInfl, category));
            if (restrictedTo is not null)
                ((IMoAffixAllomorph)entry.LexemeFormOA!).InflectionClassesRC.Add(restrictedTo);
        });
    }
}
