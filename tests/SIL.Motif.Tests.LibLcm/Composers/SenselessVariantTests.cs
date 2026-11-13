using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Sense-less variants: the narrowed last-sense refusal and the strip and author composers.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class SenselessVariantTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly LcmCache _cache = pristine.NewScratch();

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnEntryMayLoseItsLastSenseOnlyWhileItStaysAVariantOfAStem()
    {
        var verb = AddCategory(_cache, "Verb");
        var type = VariantType();
        var main = AddStem(_cache, "palit", verb);
        var solo = AddStem(_cache, "solo", verb);
        var copy = AddStem(_cache, "plit", verb);
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(copy.Entry, Id(Get<ILexEntry>(_cache, main.Entry).SensesOS[0]), type)));
        var affix = AddInflectionalPrefix(_cache, "ku", verb);
        var oddCopy = AddStem(_cache, "plat", verb);
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(oddCopy.Entry, affix.Entry, type)));

        Assert.Contains("not a variant", Assert.Throws<InvalidOperationException>(() => DeleteOnlySense(solo.Entry)).Message);
        Assert.Contains("no component", Assert.Throws<InvalidOperationException>(() => DeleteOnlySense(oddCopy.Entry)).Message);

        ApplyOps(_cache, DeleteSenseComposer.Build(_cache, DeleteIntent(copy.Entry)));
        var variant = Get<ILexEntry>(_cache, copy.Entry);
        Assert.Empty(variant.SensesOS);
        Assert.Empty(variant.MorphoSyntaxAnalysesOC);
        Assert.Single(variant.EntryRefsOS);
    }

    [Fact]
    public void StrippingRepointsTheVariantAtTheMainSenseAndKeepsItsFormAndEnvironment()
    {
        var verb = AddCategory(_cache, "Verb");
        var type = VariantType();
        var feature = ExceptionFeature("Syncopates");
        var main = AddStem(_cache, "palit", verb);
        var copy = AddStem(_cache, "plit", verb);
        var environment = AddEnvironment(_cache, "_V");
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            Get<IMoStemMsa>(_cache, main.Msa).ProdRestrictRC.Add(feature);
            ((IMoStemAllomorph)Get<ILexEntry>(_cache, copy.Entry).LexemeFormOA).PhoneEnvRC.Add(Get<IPhEnvironment>(_cache, environment));
        });
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(copy.Entry, main.Entry, type)));
        var intent = new StripVariantSensesIntent([new(copy.Entry)]);

        var report = StripVariantSensesComposer.ReadDeltas(_cache, intent);
        Assert.Contains(report, item => item.Kind == "exception-feature-delta" && item.Name.Contains("gains [Syncopates], loses []"));
        ApplyOps(_cache, StripVariantSensesComposer.Build(_cache, intent));

        var variant = Get<ILexEntry>(_cache, copy.Entry);
        var mainSense = Get<ILexEntry>(_cache, main.Entry).SensesOS[0];
        Assert.Empty(variant.SensesOS);
        Assert.Empty(variant.MorphoSyntaxAnalysesOC);
        Assert.Equal(mainSense.Guid, Assert.Single(Assert.Single(variant.EntryRefsOS).ComponentLexemesRS).Guid);
        var form = Assert.IsAssignableFrom<IMoStemAllomorph>(variant.LexemeFormOA);
        Assert.Equal("plit", form.Form.get_String(_cache.DefaultVernWs).Text);
        Assert.Equal(environment.ToGuid(), Assert.Single(form.PhoneEnvRC).Guid);
        Assert.Single(Get<ILexEntry>(_cache, main.Entry).SensesOS);
    }

    [Fact]
    public void StrippingAVariantWithTwoCopiedSensesThatShareOneAnalysisDeletesBothAndTheAnalysis()
    {
        var verb = AddCategory(_cache, "Verb");
        var type = VariantType();
        var main = AddStem(_cache, "palit", verb);
        var copy = AddStem(_cache, "plit", verb);
        var variant = Get<ILexEntry>(_cache, copy.Entry);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var second = _cache.ServiceLocator.GetInstance<ILexSenseFactory>().Create();
            variant.SensesOS.Add(second);
            second.Gloss.set_String(_cache.DefaultAnalWs, "second");
            second.MorphoSyntaxAnalysisRA = variant.SensesOS[0].MorphoSyntaxAnalysisRA;
        });
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(copy.Entry, main.Entry, type)));

        ApplyOps(_cache, StripVariantSensesComposer.Build(_cache, new StripVariantSensesIntent([new(copy.Entry)])));

        Assert.Empty(variant.SensesOS);
        Assert.Empty(variant.MorphoSyntaxAnalysesOC);
        Assert.Single(variant.EntryRefsOS);
    }

    [Fact]
    public void StrippingRefusesACopiedSenseThatSomethingElseRefersTo()
    {
        var verb = AddCategory(_cache, "Verb");
        var type = VariantType();
        var main = AddStem(_cache, "palit", verb);
        var copy = AddStem(_cache, "plit", verb);
        var other = AddStem(_cache, "plat", verb);
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(copy.Entry, main.Entry, type)));
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(other.Entry, Id(Get<ILexEntry>(_cache, copy.Entry).SensesOS[0]), type)));

        Assert.Contains("still refers", Assert.Throws<InvalidOperationException>(() =>
            StripVariantSensesComposer.Build(_cache, new StripVariantSensesIntent([new(copy.Entry)]))).Message);
        var affix = AddInflectionalPrefix(_cache, "ku", verb);
        var odd = AddStem(_cache, "kun", verb);
        ApplyOps(_cache, MakeVariantOfComposer.Build(_cache, new(odd.Entry, affix.Entry, type)));
        Assert.Contains("no stem analysis", Assert.Throws<InvalidOperationException>(() =>
            StripVariantSensesComposer.Build(_cache, new StripVariantSensesIntent([new(odd.Entry, Id(Get<ILexEntry>(_cache, affix.Entry).SensesOS[0]))]))).Message);
    }

    [Fact]
    public void AuthoringAVariantMakesAnEntryWithNoSenseAndWarnsWhenTheComponentIsNotAStem()
    {
        var verb = AddCategory(_cache, "Verb");
        var type = VariantType();
        var main = AddStem(_cache, "palit", verb);
        var environment = AddEnvironment(_cache, "_V");
        var mainSense = Id(Get<ILexEntry>(_cache, main.Entry).SensesOS[0]);
        var intent = new AuthorVariantEntryIntent(mainSense, type, "plit", Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphStem), [environment], HideMinorEntry: true);

        var created = ApplyOps(_cache, AuthorVariantEntryComposer.Build(_cache, intent));

        var variant = Get<ILexEntry>(_cache, Created(created, LexDbEntriesOperationKinds.Create));
        Assert.Empty(variant.SensesOS);
        Assert.Empty(variant.MorphoSyntaxAnalysesOC);
        var form = Assert.IsAssignableFrom<IMoStemAllomorph>(variant.LexemeFormOA);
        Assert.Equal(environment.ToGuid(), Assert.Single(form.PhoneEnvRC).Guid);
        var reference = Assert.Single(variant.EntryRefsOS);
        Assert.Equal(LexEntryRefTags.krtVariant, reference.RefType);
        Assert.Equal(1, reference.HideMinorEntry);
        Assert.Equal(mainSense.ToGuid(), Assert.Single(reference.ComponentLexemesRS).Guid);
        Assert.Empty(AuthorVariantEntryComposer.ReadWarnings(_cache, intent));

        var affix = AddInflectionalPrefix(_cache, "ku", verb);
        var odd = intent with { Component = Id(Get<ILexEntry>(_cache, affix.Entry).SensesOS[0]), Form = "kun", HideMinorEntry = false };
        Assert.Single(AuthorVariantEntryComposer.ReadWarnings(_cache, odd));
        var dry = ScratchDryRun.Of(_cache, Proposal(AuthorVariantEntryComposer.Build(_cache, odd)));
        Assert.Contains(dry.ExpectedEffects, effect => effect.Field == "dryRunWarning");
        Assert.DoesNotContain(ScratchDryRun.Of(_cache, Proposal(AuthorVariantEntryComposer.Build(_cache, intent with { Form = "pit" })))
            .ExpectedEffects, effect => effect.Field == "dryRunWarning");
    }

    private void DeleteOnlySense(CanonicalId entry) => DeleteSenseComposer.Build(_cache, DeleteIntent(entry));

    private DeleteSenseIntent DeleteIntent(CanonicalId entry) =>
        new(Id(Get<ILexEntry>(_cache, entry).SensesOS.Single()), entry);

    private CanonicalId VariantType()
    {
        ILexEntryType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var list = _cache.LangProject.LexDbOA.VariantEntryTypesOA;
            if (list is null)
            {
                list = services.GetInstance<ICmPossibilityListFactory>().Create();
                _cache.LangProject.LexDbOA.VariantEntryTypesOA = list;
            }
            type = services.GetInstance<ILexEntryTypeFactory>().Create();
            list.PossibilitiesOS.Add(type);
        });
        return Id(type);
    }

    private ICmPossibility ExceptionFeature(string name)
    {
        ICmPossibility feature = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var services = _cache.ServiceLocator;
            var data = _cache.LangProject.MorphologicalDataOA;
            data.ProdRestrictOA ??= services.GetInstance<ICmPossibilityListFactory>().Create();
            feature = services.GetInstance<ICmPossibilityFactory>().Create();
            data.ProdRestrictOA.PossibilitiesOS.Add(feature);
            feature.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return feature;
    }

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) =>
        new(new Dictionary<string, string> { ["lexical"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
