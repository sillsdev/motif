using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.Samples;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>An affix process (an infix or reduplication) keeps its exception features on its grammatical info, so the ordinary composers set them.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AffixProcessExceptionFeatureTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ADerivationalAffixProcessTakesFromAndToFeaturesAndAnInflectionalOneTakesRequiredFeatures()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var derived = AddCategory(cache, "Adjective");
        var phoneme = Phone(cache, "a");
        var derivational = ProcessMsa(cache, phoneme, new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, category, ToCategory: derived));
        var inflectional = ProcessMsa(cache, phoneme, new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, category));
        Assert.IsAssignableFrom<IMoAffixProcess>(Get<ILexEntry>(cache, derivational.Entry).LexemeFormOA);
        var features = Features(cache, "Irregular", "Loan");

        ApplyOps(cache, EditDerivationalAffixRestrictionComposer.Build(cache,
            new(derivational.Msa, FromExceptionFeatures: [features[0]], ToExceptionFeatures: [features[1]])));
        ApplyOps(cache, AddExceptionFeaturesComposer.Build(cache, new([inflectional.Msa], [features[0]])));
        ApplyOps(cache, AddExceptionFeaturesComposer.Build(cache, new([derivational.Msa], [features[1]])));

        var deriv = Get<IMoDerivAffMsa>(cache, derivational.Msa);
        Assert.Equal(new[] { features[0].ToGuid(), features[1].ToGuid() }.Order(), deriv.FromProdRestrictRC.Select(item => item.Guid).Order());
        Assert.Equal(features[1].ToGuid(), Assert.Single(deriv.ToProdRestrictRC).Guid);
        Assert.Equal(features[0].ToGuid(), Assert.Single(Get<IMoInflAffMsa>(cache, inflectional.Msa).FromProdRestrictRC).Guid);
    }

    private static (CanonicalId Entry, CanonicalId Msa) ProcessMsa(LcmCache cache, IPhPhoneme phoneme, LexicalEntryMsa msa)
    {
        var proposal = ApplyOps(cache, AuthorAffixProcessComposer.Build(cache, new AuthorAffixProcessIntent(
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "PAN", Anal(cache), msa,
            [new("a", Phoneme: Id(phoneme)), new("rest", Remaining: true)],
            [new(Insert: [Id(phoneme)]), new(Copy: "a"), new(Copy: "rest")])));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
    }

    private static CanonicalId[] Features(LcmCache cache, params string[] names)
    {
        var ids = new List<CanonicalId>();
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var morphology = cache.LangProject.MorphologicalDataOA;
            morphology.ProdRestrictOA ??= cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            foreach (var name in names)
            {
                var feature = cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
                morphology.ProdRestrictOA.PossibilitiesOS.Add(feature);
                feature.Name.set_String(cache.DefaultAnalWs, name);
                ids.Add(Id(feature));
            }
        });
        return ids.ToArray();
    }

    private static IPhPhoneme Phone(LcmCache cache, string representation)
    {
        IPhPhoneme phone = null!;
        UndoableUnitOfWorkHelper.Do("setup", "setup", cache.ServiceLocator.GetInstance<IActionHandler>(), () =>
        {
            var set = cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.FirstOrDefault();
            if (set is null)
            {
                set = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            }
            phone = cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            set.PhonemesOC.Add(phone);
            phone.CodesOS.Clear();
            var code = cache.ServiceLocator.GetInstance<IPhCodeFactory>().Create();
            phone.CodesOS.Add(code);
            code.Representation.set_String(cache.DefaultVernWs, representation);
        });
        return phone;
    }
}
