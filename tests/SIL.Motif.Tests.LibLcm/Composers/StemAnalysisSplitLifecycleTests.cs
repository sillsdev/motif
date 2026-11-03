using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Retirement;
using SIL.Motif.Projection.Retirement;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Retirement;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>What Undo, retiring an allomorph and deleting a sense do to a stem split by exception features.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class StemAnalysisSplitLifecycleTests(PristineProjectFixture pristine)
{
    [Fact]
    public void UndoingTheSplitRestoresTheSingleTaggedAnalysisAndSense()
    {
        using var cache = pristine.NewScratch();
        var (stem, _, first, _) = Split(cache, apply: false, out var split);

        ApplyOps(cache, split);
        Assert.Equal(2, Get<ILexEntry>(cache, stem.Entry).MorphoSyntaxAnalysesOC.Count);
        cache.ActionHandlerAccessor.Undo();

        var entry = Get<ILexEntry>(cache, stem.Entry);
        Assert.Single(entry.MorphoSyntaxAnalysesOC);
        Assert.Single(entry.SensesOS);
        Assert.Equal([first.ToGuid()], Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC.Select(item => item.Guid));
    }

    [Fact]
    public void AnAlternateFormOfASplitStemCanBeRetiredAndBothAnalysesStay()
    {
        using var cache = pristine.NewScratch();
        var (stem, _, _, _) = Split(cache, apply: true, out _);
        var entry = Get<ILexEntry>(cache, stem.Entry);
        IMoStemAllomorph retired = null!, survivor = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var type = cache.ServiceLocator.GetInstance<IMoMorphTypeRepository>().GetObject(MoMorphTypeTags.kguidMorphStem);
            retired = Alternate(cache, entry, type, "hagar");
            survivor = Alternate(cache, entry, type, "hagar");
        });
        var document = new RetireAllomorphIntentDocument(
            new RetireAllomorphIntent(Id(entry).Value, AllomorphRetirementScope.Stem, [Identity(cache, entry, retired)], [], [], []),
            [Identity(cache, entry, survivor)]);

        ApplyOps(cache, RetireAllomorphComposer.Build(cache, document, () => CanonicalId.Mint()));

        Assert.DoesNotContain(retired.Guid, entry.AlternateFormsOS.Select(item => item.Guid));
        Assert.Contains(survivor.Guid, entry.AlternateFormsOS.Select(item => item.Guid));
        Assert.Equal(2, entry.MorphoSyntaxAnalysesOC.Count);
        Assert.Equal(2, entry.SensesOS.Count);
    }

    [Fact]
    public void DeletingTheSenseOfTheSecondAnalysisDropsThatAnalysisAndKeepsTheFirstTag()
    {
        using var cache = pristine.NewScratch();
        var (stem, second, first, _) = Split(cache, apply: true, out _);
        var entry = Get<ILexEntry>(cache, stem.Entry);
        Assert.Equal(second.ToGuid(), entry.SensesOS[1].MorphoSyntaxAnalysisRA.Guid);

        ApplyOps(cache, DeleteSenseComposer.Build(cache, new DeleteSenseIntent(Id(entry.SensesOS[1]), Id(entry))));

        Assert.Single(entry.SensesOS);
        Assert.Equal(stem.Msa.ToGuid(), Assert.Single(entry.MorphoSyntaxAnalysesOC).Guid);
        Assert.Equal([first.ToGuid()], Get<IMoStemMsa>(cache, stem.Msa).ProdRestrictRC.Select(item => item.Guid));
    }

    private static ((CanonicalId Entry, CanonicalId Msa, CanonicalId Form) Stem, CanonicalId Second, CanonicalId First,
        CanonicalId Other) Split(LcmCache cache, bool apply, out IReadOnlyList<OperationEnvelope> operations)
    {
        var ws = Anal(cache);
        var verb = AddCategory(cache, "Verb");
        var stem = AddStem(cache, "hagad", verb);
        var first = Feature(cache, "Syncope", "sy");
        var other = Feature(cache, "Rhotacism", "rh");
        ApplyOps(cache, EditExceptionFeaturesComposer.Build(cache, new(stem.Msa, [first])));
        operations = AddMorphoSyntaxAnalysisComposer.Build(cache, new(stem.Entry,
            new LexicalEntryMsa(LexicalEntryMsaKind.Stem, verb, ExceptionFeatures: [other]),
            Gloss: "hagad again", AnalysisWritingSystem: ws));
        var second = default(CanonicalId);
        if (apply) second = Created(ApplyOps(cache, operations), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        return (stem, second, first, other);
    }

    private static IMoStemAllomorph Alternate(LcmCache cache, ILexEntry entry, IMoMorphType type, string form)
    {
        var allomorph = cache.ServiceLocator.GetInstance<IMoStemAllomorphFactory>().Create();
        entry.AlternateFormsOS.Add(allomorph);
        allomorph.MorphTypeRA = type;
        allomorph.Form.set_String(cache.DefaultVernWs, form);
        return allomorph;
    }

    private static AllomorphIdentity Identity(LcmCache cache, ILexEntry entry, IMoStemAllomorph form) => new(
        CanonicalId.FromGuid(form.Guid).Value, CanonicalId.FromGuid(entry.Guid).Value, form.ClassName, "alternate", "stem",
        AllomorphRetirementSemanticDigest.Compute(cache, form), null);

    private static CanonicalId Feature(LcmCache cache, string name, string abbreviation) =>
        Created(ApplyOps(cache, AuthorExceptionFeatureComposer.Build(cache, new(name, Anal(cache), abbreviation))),
            CmPossibilityListPossibilitiesOperationKinds.Create);
}
