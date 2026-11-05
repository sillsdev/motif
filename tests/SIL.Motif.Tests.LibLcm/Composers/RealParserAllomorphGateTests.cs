using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using Xunit.Abstractions;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.RealParserReadings;

namespace SIL.Motif.Tests.Composers;

/// <summary>
/// What the real parser does with the conditions an allomorph of a derivational affix carries beside its entry's
/// own: the stem features the allomorph requires (MsEnvFeatures) and the inflection classes it lists. The gates are
/// set straight on LibLCM objects, because Motif has no composer for them; the language is synthetic and
/// Swahili-shaped (one derivational prefix whose two forms go with transitive and intransitive stems).
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class RealParserAllomorphGateTests(PristineProjectFixture pristine, ITestOutputHelper output)
{
    private static readonly string[] Letters = ["p", "a", "g", "k", "u", "n", "l", "i", "t"];

    private sealed record Setup(LcmCache Cache, Dictionary<CanonicalId, string> Names, ILexEntry Prefix,
        IFsClosedFeature Feature, IFsSymFeatVal Transitive, IFsSymFeatVal Intransitive);

    private Setup Build(bool classes)
    {
        var cache = pristine.NewScratch();
        RealParserProject.PrepareForParsing(cache, Letters);
        var ws = Anal(cache);
        var verb = Created(ApplyOps(cache, AuthorPartOfSpeechComposer.Build(cache,
            new AuthorPartOfSpeechIntent("Verb", ws, "V"))), CmPossibilityListPossibilitiesOperationKinds.Create);
        var featureProposal = ApplyOps(cache, AuthorInflectionFeatureComposer.Build(cache,
            new("transitivity", ws, [new("Intransitive", "intr"), new("Transitive", "tr")], "tv")));
        var feature = Created(featureProposal, FsFeatureSystemFeaturesOperationKinds.Create);
        var values = featureProposal.Operations.Where(op => op.Kind == FsClosedFeatureValuesOperationKinds.Create)
            .Select(op => (IFsSymFeatVal)Get<ICmObject>(cache, op.EntityId!.Value)).ToArray();
        var intransitive = values.Single(value => value.Name.get_String(cache.DefaultAnalWs).Text == "Intransitive");
        var transitive = values.Single(value => value.Name.get_String(cache.DefaultAnalWs).Text == "Transitive");
        CanonicalId? classA = null, classB = null;
        if (classes)
        {
            classA = AddInflectionClass(cache, verb, "A");
            classB = AddInflectionClass(cache, verb, "B");
        }
        var names = new Dictionary<CanonicalId, string>(CanonicalId.ByGuid);
        foreach (var (stem, value, inflectionClass) in new[]
                 { ("kaun", intransitive, classA), ("lilit", transitive, classB) })
        {
            var added = AddStem(cache, stem, verb, inflectionClass);
            names[added.Form] = stem;
            var structure = Created(ApplyOps(cache, AuthorFeatureStructureComposer.Build(cache, new(added.Msa))),
                MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
            ApplyOps(cache, AuthorFeatureValueComposer.Build(cache, new(structure, feature, Id(value))));
        }
        var created = ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new AuthorLexicalEntryIntent("pag",
            Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "causative", ws,
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, verb, verb))));
        var entry = Created(created, LexDbEntriesOperationKinds.Create);
        names[Created(created, LexEntryLexemeFormOperationKinds.CreateLexemeForm)] = "pag";
        var alternate = ApplyOps(cache, AuthorAlternateFormComposer.Build(cache, new AuthorAlternateFormIntent(entry,
            "pa", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix))));
        names[Created(alternate, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms)] = "pa";
        return new(cache, names, Get<ILexEntry>(cache, entry),
            (IFsClosedFeature)Get<ICmObject>(cache, feature), transitive, intransitive);
    }

    private static void Require(LcmCache cache, IMoAffixAllomorph form, IFsClosedFeature feature, IFsSymFeatVal value) =>
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var structure = cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            form.MsEnvFeaturesOA = structure;
            var spec = cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            structure.FeatureSpecsOC.Add(spec);
            spec.FeatureRA = feature;
            spec.ValueRA = value;
        });

    [RealParserFact]
    public async Task AnAllomorphOfADerivationalAffixIsChosenByTheStemFeaturesItRequires()
    {
        var setup = Build(classes: false);
        using var cache = setup.Cache;
        var plain = setup.Prefix.AlternateFormsOS.Single() as IMoAffixAllomorph;
        var lexeme = (IMoAffixAllomorph)setup.Prefix.LexemeFormOA;
        Assert.NotNull(plain);

        // Neither form is conditioned: both stems take both forms.
        Assert.Equal(["pa+kaun"], await Parse(cache, output, setup.Names, "pakaun"));
        Assert.Equal(["pag+kaun"], await Parse(cache, output, setup.Names, "pagkaun"));
        Assert.Equal(["pa+lilit"], await Parse(cache, output, setup.Names, "palilit"));
        Assert.Equal(["pag+lilit"], await Parse(cache, output, setup.Names, "paglilit"));

        Require(cache, lexeme, setup.Feature, setup.Transitive);
        Require(cache, plain!, setup.Feature, setup.Intransitive);
        Assert.Equal(["pa+kaun"], await Parse(cache, output, setup.Names, "pakaun"));
        Assert.Equal(["pag+lilit"], await Parse(cache, output, setup.Names, "paglilit"));
        Assert.Empty(await Parse(cache, output, setup.Names, "pagkaun"));
        Assert.Empty(await Parse(cache, output, setup.Names, "palilit"));
    }

    [RealParserFact]
    public async Task InflectionClassesOnAnAllomorphOfADerivationalAffixAreNotReadByTheParser()
    {
        var setup = Build(classes: true);
        using var cache = setup.Cache;
        var plain = (IMoAffixAllomorph)setup.Prefix.AlternateFormsOS.Single();
        var verb = cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.OfType<IPartOfSpeech>().Single(item =>
            item.Name.get_String(cache.DefaultAnalWs).Text == "Verb");
        var classA = verb!.InflectionClassesOC.Single(item => item.Name.get_String(cache.DefaultAnalWs).Text == "A");
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => plain.InflectionClassesRC.Add(classA));

        // pa is listed for class A only (kaun), yet the derivational entry's parse is the one the MSA decides.
        Assert.Equal(["pa+lilit"], await Parse(cache, output, setup.Names, "palilit"));
        Assert.Equal(["pa+kaun"], await Parse(cache, output, setup.Names, "pakaun"));
    }
}
