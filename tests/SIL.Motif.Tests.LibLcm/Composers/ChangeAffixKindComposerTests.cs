using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

/// <summary>Changing an existing affix between inflectional, derivational and unclassified in place.</summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class ChangeAffixKindComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public ChangeAffixKindComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AnInflectionalAffixBecomesDerivationalKeepingItsCategoryAndSenseAndDroppingTheSlotWithAWarning()
    {
        var slot = AddSlot("Outer");
        var (entry, msa) = InflectionalPrefix("mi", slot);
        var intent = new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational);

        var operations = ChangeAffixKindComposer.Build(_cache, intent);

        Assert.Equal([LexEntryMorphoSyntaxAnalysesOperationKinds.Create,
                MoDerivAffMsaFromPartOfSpeechOperationKinds.SetFromPartOfSpeech,
                MoDerivAffMsaToPartOfSpeechOperationKinds.SetToPartOfSpeech,
                LexSenseMorphoSyntaxAnalysisOperationKinds.SetMorphoSyntaxAnalysis,
                MorphoSyntaxAnalysisDeleteOperationKinds.Delete],
            operations.Select(operation => operation.Kind));
        var warning = Assert.Single(ChangeAffixKindComposer.ReadWarnings(_cache, intent));
        Assert.Equal(("slot", slot.Value), (warning.Kind, warning.Id));
        Assert.Contains("Outer", warning.Name, StringComparison.Ordinal);

        var proposal = ApplyOps(_cache, operations);
        var created = Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var reloaded = Get<ILexEntry>(_cache, entry);
        Assert.False(Exists(_cache, msa));
        var derivational = Assert.IsAssignableFrom<IMoDerivAffMsa>(Assert.Single(reloaded.MorphoSyntaxAnalysesOC));
        Assert.Equal(created.ToGuid(), derivational.Guid);
        Assert.Equal(_category.ToGuid(), derivational.FromPartOfSpeechRA!.Guid);
        Assert.Equal(_category.ToGuid(), derivational.ToPartOfSpeechRA!.Guid);
        Assert.Equal(derivational.Guid, Assert.Single(reloaded.SensesOS).MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void ADerivationalAffixFlipsBackToInflectionalInASlotAndTheEntryKeepsOneInfo()
    {
        var slot = AddSlot("Outer");
        var (entry, msa) = InflectionalPrefix("mi", slot);
        var derivational = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational))), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        var back = new ChangeAffixKindIntent(derivational, LexicalEntryMsaKind.Inflectional, Slots: [slot]);
        Assert.Empty(ChangeAffixKindComposer.ReadWarnings(_cache, back));
        var inflectional = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache, back)),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        var reloaded = Get<ILexEntry>(_cache, entry);
        var info = Assert.IsAssignableFrom<IMoInflAffMsa>(Assert.Single(reloaded.MorphoSyntaxAnalysesOC));
        Assert.Equal(inflectional.ToGuid(), info.Guid);
        Assert.Equal(_category.ToGuid(), info.PartOfSpeechRA!.Guid);
        Assert.Equal([slot.ToGuid()], info.SlotsRC.Select(item => item.Guid));
        Assert.Equal(info.Guid, Assert.Single(reloaded.SensesOS).MorphoSyntaxAnalysisRA.Guid);
    }

    [Fact]
    public void ADerivationalAffixCanTakeAnotherOutputCategoryAndLosesItWithAWarningWhenItStopsBeingDerivational()
    {
        var adjective = AddCategory(_cache, "Adjective");
        var (_, msa) = InflectionalPrefix("mi");
        var derivational = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational, ToCategory: adjective))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Equal(adjective.ToGuid(), Get<IMoDerivAffMsa>(_cache, derivational).ToPartOfSpeechRA!.Guid);

        var warnings = ChangeAffixKindComposer.ReadWarnings(_cache,
            new ChangeAffixKindIntent(derivational, LexicalEntryMsaKind.Unclassified));

        var warning = Assert.Single(warnings);
        Assert.Equal(("category", adjective.Value), (warning.Kind, warning.Id));
        var unclassified = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(derivational, LexicalEntryMsaKind.Unclassified))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Equal(_category.ToGuid(), Get<IMoUnclassifiedAffixMsa>(_cache, unclassified).PartOfSpeechRA!.Guid);
    }

    [Fact]
    public void ExceptionFeaturesCarryBetweenInflectionalAndDerivationalAndFormClassesAreReported()
    {
        var feature = ExceptionFeature("Irregular");
        var cls = AddInflectionClass(_cache, _category, "Class 1");
        var (_, msa) = InflectionalPrefix("mi");
        var form = FormOf(msa);
        ApplyOps(_cache, [new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaFromProdRestrictOperationKinds.AddRefFromProdRestrict,
            target: msa, after: JsonSerializer.SerializeToElement(new { member = feature.Value }))]);
        ApplyOps(_cache, EditAffixInflectionClassesComposer.Build(_cache,
            new(form, [], [cls])));

        var intent = new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational);
        var warning = Assert.Single(ChangeAffixKindComposer.ReadWarnings(_cache, intent));
        Assert.Equal(("inflection-class", cls.Value), (warning.Kind, warning.Id));
        var derivational = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache, intent)),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        Assert.Equal([feature.ToGuid()], Get<IMoDerivAffMsa>(_cache, derivational).FromProdRestrictRC.Select(item => item.Guid));
        var formClasses = Assert.IsAssignableFrom<IMoAffixAllomorph>(Get<IMoForm>(_cache, form)).InflectionClassesRC;
        Assert.Equal([cls.ToGuid()], formClasses.Select(item => item.Guid));
    }

    [Fact]
    public void ASenseOfAnotherInfoOnTheSameEntryIsLeftAlone()
    {
        var (entry, msa) = InflectionalPrefix("mi");
        var other = Get<ILexEntry>(_cache, entry);
        Assert.Single(other.MorphoSyntaxAnalysesOC);
        ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache, new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Unclassified)));
        Assert.Single(Get<ILexEntry>(_cache, entry).MorphoSyntaxAnalysesOC);
    }

    [Fact]
    public void TheSameKindStagesNothingAndAStemInfoIsRefused()
    {
        var (_, msa) = InflectionalPrefix("mi");
        Assert.Empty(ChangeAffixKindComposer.Build(_cache, new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Inflectional)));

        var (_, stem, _) = AddStem(_cache, "kaun", _category);
        var notAffix = Assert.Throws<InvalidOperationException>(() =>
            ChangeAffixKindComposer.Build(_cache, new ChangeAffixKindIntent(stem, LexicalEntryMsaKind.Derivational)));
        Assert.Contains("not the grammatical info of an affix", notAffix.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAdhocProhibitionAndAMorphBundleThatUseTheInfoAreRepointedInPlaceAndFlipBack()
    {
        var (_, msa) = InflectionalPrefix("mi");
        var (_, other, _) = AddStem(_cache, "kaun", _category);
        var (_, third, _) = AddStem(_cache, "bago", _category);
        var asFirst = Created(ApplyOps(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", msa, [other, third], "anywhere"))), AdhocProhibitionCreateOperationKinds.Create);
        var inRest = Created(ApplyOps(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", other, [third, msa], "anywhere"))), AdhocProhibitionCreateOperationKinds.Create);
        var bundle = AddBundle(msa);
        var intent = new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational);

        var warnings = ChangeAffixKindComposer.ReadWarnings(_cache, intent);
        Assert.Contains(warnings, item => item.Kind == "ad-hoc-prohibition" && item.Id == asFirst.Value);
        Assert.Contains(warnings, item => item.Kind == "ad-hoc-prohibition" && item.Id == inRest.Value);
        Assert.Contains(warnings, item => item.Kind == "morph-bundles" && item.Name.StartsWith("1 ", StringComparison.Ordinal));

        var operations = ChangeAffixKindComposer.Build(_cache, intent);
        var dry = ScratchDryRun.Of(_cache, Proposal(operations));
        Assert.Contains(dry.ExpectedEffects, effect => effect.Field == SnapshotFields.WfiMorphBundleMsa);
        Assert.Contains(dry.ExpectedEffects, effect => effect.Field == SnapshotFields.MoMorphAdhocProhibFirstMorpheme);
        Assert.Contains(dry.ExpectedEffects, effect => effect.Field == SnapshotFields.MoMorphAdhocProhibRestOfMorphs);

        var derivational = Created(ApplyOps(_cache, operations), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.False(Exists(_cache, msa));
        Assert.Equal(derivational.ToGuid(), Get<IMoMorphAdhocProhib>(_cache, asFirst).FirstMorphemeRA.Guid);
        Assert.Equal([third.ToGuid(), derivational.ToGuid()], Get<IMoMorphAdhocProhib>(_cache, inRest).RestOfMorphsRS.Select(item => item.Guid));
        Assert.Equal(derivational.ToGuid(), Get<IWfiMorphBundle>(_cache, bundle).MsaRA.Guid);

        var back = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(derivational, LexicalEntryMsaKind.Inflectional))), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        Assert.Equal(back.ToGuid(), Get<IMoMorphAdhocProhib>(_cache, asFirst).FirstMorphemeRA.Guid);
        Assert.Equal([third.ToGuid(), back.ToGuid()], Get<IMoMorphAdhocProhib>(_cache, inRest).RestOfMorphsRS.Select(item => item.Guid));
        Assert.Equal(back.ToGuid(), Get<IWfiMorphBundle>(_cache, bundle).MsaRA.Guid);
    }

    [Fact]
    public void ARawRetargetThatNamesAnAbsentInfoOrAnInfoOfAnotherEntryOrAStemIsRefused()
    {
        var (_, msa) = InflectionalPrefix("mi");
        var (_, otherAffix) = InflectionalPrefix("pa");
        var (_, stem, _) = AddStem(_cache, "kaun", _category);
        var rule = Created(ApplyOps(_cache, AuthorAdhocProhibitionComposer.Build(_cache,
            new("morpheme", msa, [stem], "anywhere"))), AdhocProhibitionCreateOperationKinds.Create);
        var bundle = AddBundle(msa);

        InvalidOperationException Refused(string kind, CanonicalId target, CanonicalId from, CanonicalId to) =>
            Assert.Throws<InvalidOperationException>(() => ApplyOps(_cache,
            [new OperationEnvelope(CanonicalId.Mint(), kind, target: target,
                after: JsonSerializer.SerializeToElement(new { from = from.Value, to = to.Value }))]));

        Assert.Contains("no longer names", Refused(MsaReferenceRetargetOperationKinds.SetBundleMsa, bundle, otherAffix, msa).Message,
            StringComparison.Ordinal);
        Assert.Contains("no longer names", Refused(MsaReferenceRetargetOperationKinds.RetargetProhibitionMorphemes, rule,
            otherAffix, msa).Message, StringComparison.Ordinal);
        Assert.Contains("must belong", Refused(MsaReferenceRetargetOperationKinds.SetBundleMsa, bundle, msa, otherAffix).Message,
            StringComparison.Ordinal);
        Assert.Contains("must belong", Refused(MsaReferenceRetargetOperationKinds.SetBundleMsa, bundle, msa, stem).Message,
            StringComparison.Ordinal);
        Assert.Equal(msa.ToGuid(), Get<IWfiMorphBundle>(_cache, bundle).MsaRA.Guid);
    }

    private CanonicalId AddBundle(CanonicalId msa)
    {
        IWfiMorphBundle bundle = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var wordform = _cache.ServiceLocator.GetInstance<IWfiWordformFactory>().Create(
                TsStringUtils.MakeString("mikaun", _cache.DefaultVernWs));
            var analysis = _cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
            wordform.AnalysesOC.Add(analysis);
            bundle = _cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
            analysis.MorphBundlesOS.Add(bundle);
            bundle.MsaRA = Get<IMoMorphSynAnalysis>(_cache, msa);
        });
        return Id(bundle);
    }

    [Fact]
    public void ASlotOfAnotherCategoryIsRefusedAndExtrasThatDoNotFitTheKindAreRejected()
    {
        var other = AddCategory(_cache, "Adjective");
        var foreignSlot = Created(ApplyOps(_cache, AuthorAffixSlotComposer.Build(_cache,
            new AuthorAffixSlotIntent(other, "Foreign", Anal(_cache), true))), PartOfSpeechAffixSlotsOperationKinds.Create);
        var (_, msa) = InflectionalPrefix("mi");
        var derivational = Created(ApplyOps(_cache, ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(msa, LexicalEntryMsaKind.Derivational))), LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

        Assert.Throws<InvalidOperationException>(() => ChangeAffixKindComposer.Build(_cache,
            new ChangeAffixKindIntent(derivational, LexicalEntryMsaKind.Inflectional, Slots: [foreignSlot])));
        Assert.Throws<ContractParseException>(() => ChangeAffixKindIntentParser.Parse(
            JsonDocument.Parse($$"""{"msa":"{{msa.Value}}","kind":"inflectional","toCategory":"{{other.Value}}"}""").RootElement));
        Assert.Throws<ContractParseException>(() => ChangeAffixKindIntentParser.Parse(
            JsonDocument.Parse($$"""{"msa":"{{msa.Value}}","kind":"stem"}""").RootElement));
        Assert.Throws<ContractParseException>(() => ChangeAffixKindIntentParser.Parse(
            JsonDocument.Parse($$"""{"msa":"{{msa.Value}}","kind":"derivational","slots":[]}""").RootElement));
    }

    private CanonicalId FormOf(CanonicalId msa) =>
        Id(Get<IMoMorphSynAnalysis>(_cache, msa).Owner is ILexEntry entry ? entry.LexemeFormOA : throw new InvalidOperationException());

    private CanonicalId AddSlot(string name) => Created(ApplyOps(_cache, AuthorAffixSlotComposer.Build(_cache,
        new AuthorAffixSlotIntent(_category, name, Anal(_cache), true))), PartOfSpeechAffixSlotsOperationKinds.Create);

    private (CanonicalId Entry, CanonicalId Msa) InflectionalPrefix(string form, params CanonicalId[] slots)
    {
        var proposal = ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent(form, Vern(_cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), form, Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Inflectional, _category, Slots: slots.Length == 0 ? null : slots))));
        return (Created(proposal, LexDbEntriesOperationKinds.Create),
            Created(proposal, LexEntryMorphoSyntaxAnalysesOperationKinds.Create));
    }

    private CanonicalId ExceptionFeature(string name)
    {
        ICmPossibility feature = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var morphology = _cache.LangProject.MorphologicalDataOA;
            morphology.ProdRestrictOA ??= _cache.ServiceLocator.GetInstance<ICmPossibilityListFactory>().Create();
            feature = _cache.ServiceLocator.GetInstance<ICmPossibilityFactory>().Create();
            morphology.ProdRestrictOA.PossibilitiesOS.Add(feature);
            feature.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return Id(feature);
    }
}
