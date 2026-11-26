using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Commands.Review;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

/// <summary>Pins which forms a touched object stands for in a real Baseline project copy.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class TrialSiblingFormsTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AnEntryAndAnythingItOwnsStandForAllOfTheEntrysForms()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var expected = entry.LexemeFormOA!.Form.VernacularDefaultWritingSystem.Text;

        var fromEntry = TrialSiblingResolver.ExistingForms(cache, seed.FirstEntryId).ToArray();
        var fromForm = TrialSiblingResolver.ExistingForms(cache, seed.FirstLexemeFormId).ToArray();
        var fromSense = TrialSiblingResolver.ExistingForms(cache, entry.SensesOS[0].Guid).ToArray();

        Assert.Contains(expected, fromEntry);
        Assert.Equal(fromEntry, fromForm);
        Assert.Equal(fromEntry, fromSense);
        Assert.DoesNotContain(seed.SecondLexemeFormId.ToString(), fromEntry);
    }

    [Fact]
    public void AnObjectNoEntryOwnsOrTheBaselineLacksStandsForNothing()
    {
        using var cache = pristine.NewScratch();

        Assert.Empty(TrialSiblingResolver.ExistingForms(cache, pristine.Seed.PartOfSpeechId));
        Assert.Empty(TrialSiblingResolver.ExistingForms(cache, Guid.NewGuid()));
    }

    [Fact]
    public void ABareFormTakesItsAffixKindFromTheMorphTypeAndAnExplicitMarkWins()
    {
        using var cache = pristine.NewScratch();
        var seed = pristine.Seed;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(seed.FirstEntryId);
        var bare = entry.LexemeFormOA!.Form.VernacularDefaultWritingSystem.Text;
        var types = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>();
        void Set(System.Guid type) => NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            entry.LexemeFormOA!.MorphTypeRA = types.GetObject(type));
        string[] Forms() => [.. TrialSiblingResolver.ExistingForms(cache, seed.FirstEntryId)];

        Assert.Contains(bare, Forms());
        Set(MoMorphTypeTags.kguidMorphSuffix);
        Assert.Contains("-" + bare, Forms());
        Set(MoMorphTypeTags.kguidMorphEnclitic);
        Assert.Contains("-" + bare, Forms());
        Set(MoMorphTypeTags.kguidMorphProclitic);
        Assert.Contains(bare + "-", Forms());
        Set(MoMorphTypeTags.kguidMorphPrefixingInterfix);
        Assert.Contains(bare + "-", Forms());
        Set(MoMorphTypeTags.kguidMorphInfix);
        Assert.Contains(bare, Forms());
        Set(MoMorphTypeTags.kguidMorphSuffix);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            entry.LexemeFormOA!.Form.set_String(cache.DefaultVernWs, bare + "="));
        Assert.Contains(bare + "=", Forms());
    }
}
