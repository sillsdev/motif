using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group3)]
public sealed class AffixPrerequisiteTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("class")]
    [InlineData("category")]
    [InlineData("environment")]
    public void ReferencesOnNewTargetsBindTheirExistingPrerequisites(string changed)
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "one");
        var env = AddEnvironment(cache, "context");
        var operations = AuthorLexicalEntryComposer.Build(cache, new("m", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "one", Anal(cache),
            new(LexicalEntryMsaKind.Inflectional, category),
            InflectionClasses: [cls], Environments: [env]));
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            switch (changed)
            {
                case "class": Get<IMoInflClass>(cache, cls).Name.set_String(cache.DefaultAnalWs, "two"); break;
                case "category": Get<IPartOfSpeech>(cache, category).Name.set_String(cache.DefaultAnalWs, "changed"); break;
                default: Get<IPhEnvironment>(cache, env).Name.set_String(cache.DefaultAnalWs, "changed"); break;
            }
        });
        var digest = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "prerequisite-tests"));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(cache));
        Assert.False(Exists(cache, Created(proposal, LexDbEntriesOperationKinds.Create)));
        ApplyOps(cache, operations);
    }

    [Fact]
    public void UnrelatedLexicalEditDoesNotInvalidateAnAlternate()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var (entry, _, _) = AddInflectionalPrefix(cache, "m", category);
        var (other, _, _) = AddStem(cache, "tu", category);
        var proposal = Proposal(AuthorAlternateFormComposer.Build(cache,
            new(entry, "mw", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix))));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<ILexEntry>(cache, other).LexemeFormOA.Form.set_String(cache.DefaultVernWs, "new"));
        ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "prerequisite-tests");
        Assert.Equal("new", Get<ILexEntry>(cache, other).LexemeFormOA.Form.get_String(cache.DefaultVernWs).Text);
    }
    [Theory]
    [InlineData("membership", false)]
    [InlineData("code", false)]
    [InlineData("pattern", false)]
    [InlineData("firstSet", false)]
    [InlineData("defaultVernacular", false)]
    [InlineData("membership", true)]
    [InlineData("code", true)]
    public void SoundMeaningChangesInvalidateExistingOrStagedConditions(string changed, bool staged)
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var phone = Created(ApplyOps(cache, AuthorPhonemeComposer.Build(cache,
            new("vowel", ["a"]))), PhPhonemeSetPhonemesOperationKinds.Create);
        var other = Created(ApplyOps(cache, AuthorPhonemeComposer.Build(cache,
            new("other vowel", ["i"]))), PhPhonemeSetPhonemesOperationKinds.Create);
        var cls = Created(ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new("vowels", "V", [phone]))), PhPhonDataNaturalClassesOperationKinds.Create);
        var envOps = AuthorEnvironmentComposer.Build(cache, new("after vowel", [new(NaturalClass: cls)], []));
        var env = envOps.Single(operation => operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create).EntityId!.Value;
        if (!staged) ApplyOps(cache, envOps);
        var (entry, _, _) = AddInflectionalPrefix(cache, "m", category);
        SIL.Motif.Contract.Model.Proposal proposal;
        if (staged)
        {
            var form = CanonicalId.Mint();
            var create = new SIL.Motif.Contract.Model.OperationEnvelope(CanonicalId.Mint(),
                LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms, target: entry, entityId: form,
                after: System.Text.Json.JsonSerializer.SerializeToElement(new
                    { morphType = MorphType(MoMorphTypeTags.kguidMorphPrefix).Value, ws = Vern(cache), text = "mw" }));
            var link = new SIL.Motif.Contract.Model.OperationEnvelope(CanonicalId.Mint(),
                MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv, target: form,
                after: System.Text.Json.JsonSerializer.SerializeToElement(new { member = env.Value }),
                dependsOn: [new(create.OperationId), new(envOps[0].OperationId)]);
            proposal = Proposal(envOps.Concat([create, link]).ToArray());
        }
        else proposal = Proposal(AuthorAlternateFormComposer.Build(cache,
            new(entry, "mw", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), [env])));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            switch (changed)
            {
                case "membership": Get<IPhNCSegments>(cache, cls).SegmentsRC.Add(Get<IPhPhoneme>(cache, other)); break;
                case "code": Get<IPhPhoneme>(cache, phone).CodesOS[0].Representation.set_String(cache.DefaultVernWs, "e"); break;
                case "pattern": Get<IPhEnvironment>(cache, env).StringRepresentation =
                    SIL.LCModel.Core.Text.TsStringUtils.MakeString("/ _ [V]", cache.DefaultVernWs); break;
                case "defaultVernacular":
                    cache.ServiceLocator.WritingSystemManager.GetOrSet("qaa-x-other", out var writingSystem);
                    cache.ServiceLocator.WritingSystems.VernacularWritingSystems.Add(writingSystem);
                    cache.ServiceLocator.WritingSystems.CurrentVernacularWritingSystems.Insert(0, writingSystem);
                    break;
                default:
                    var set = cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
                    cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Insert(0, set);
                    break;
            }
        });
        Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "sound-prerequisite-tests"));
        Assert.Empty(Get<ILexEntry>(cache, entry).AlternateFormsOS);
    }

    [Fact]
    public void FeatureValuesInvalidateExistingAndStagedEnvironmentPrerequisites()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var (selected, otherValue) = AddPhonologicalFeatureValues(cache);
        var phonemeProposal = ApplyOps(cache, AuthorPhonemeComposer.Build(cache,
            new("feature-bearing phoneme", ["a"], [selected])));
        var phoneme = Created(phonemeProposal, PhPhonemeSetPhonemesOperationKinds.Create);
        var classProposal = ApplyOps(cache, AuthorNaturalClassComposer.Build(cache,
            new("feature class", "V", Features: [selected])));
        var naturalClass = Created(classProposal, PhPhonDataNaturalClassesOperationKinds.Create);
        AssertClosedValue(Get<IPhPhoneme>(cache, phoneme).FeaturesOA!, selected.Feature, selected.Value);
        AssertClosedValue(Get<IPhNCFeatures>(cache, naturalClass).FeaturesOA!, selected.Feature, selected.Value);

        var existingEnvironmentProposal = ApplyOps(cache, AuthorEnvironmentComposer.Build(cache,
            new("existing feature-class environment", [new(NaturalClass: naturalClass)], [])));
        var existingEnvironment = Created(existingEnvironmentProposal, PhPhonDataEnvironmentsOperationKinds.Create);
        var (existingEntry, _, _) = AddInflectionalPrefix(cache, "m", category);
        var existingProposal = Proposal(AuthorAlternateFormComposer.Build(cache,
            new(existingEntry, "mw", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), [existingEnvironment])));
        var existingForm = Created(existingProposal, LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        var existingDryRun = ScratchDryRun.Of(cache, existingProposal);
        ChangeClosedValue(cache, Get<IPhNCFeatures>(cache, naturalClass).FeaturesOA!, selected.Feature, otherValue);
        var beforeExistingRefusal = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache, existingProposal,
            existingDryRun.Anchor, "feature-prerequisite-tests"));
        Assert.Equal(beforeExistingRefusal, BaselineSemanticDigest.Compute(cache));
        Assert.False(Exists(cache, existingForm));
        Assert.Empty(Get<ILexEntry>(cache, existingEntry).AlternateFormsOS);
        var freshExistingDryRun = ScratchDryRun.Of(cache, existingProposal);
        Assert.Equal(beforeExistingRefusal, BaselineSemanticDigest.Compute(cache));
        Assert.False(ProposalApplier.Apply(cache, existingProposal, freshExistingDryRun.Anchor,
            "feature-prerequisite-tests").AlreadyApplied);
        AssertAlternateReadback(cache, existingEntry, existingForm, existingEnvironment, "mw");
        AssertClosedValue(Get<IPhNCFeatures>(cache, naturalClass).FeaturesOA!, selected.Feature, otherValue);

        var (stagedEntry, _, _) = AddInflectionalPrefix(cache, "n", category);
        var stagedEnvironmentOperations = AuthorEnvironmentComposer.Build(cache,
            new("staged feature-class environment", [new(NaturalClass: naturalClass)], []));
        var stagedEnvironmentCreate = Assert.Single(stagedEnvironmentOperations,
            operation => operation.Kind == PhPhonDataEnvironmentsOperationKinds.Create);
        var stagedEnvironment = stagedEnvironmentCreate.EntityId!.Value;
        var stagedAlternateOperations = AuthorAlternateFormComposer.Build(cache,
            new(stagedEntry, "nw", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix)));
        var stagedAlternateCreate = Assert.Single(stagedAlternateOperations,
            operation => operation.Kind == LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms);
        var stagedForm = stagedAlternateCreate.EntityId!.Value;
        var environmentLink = new SIL.Motif.Contract.Model.OperationEnvelope(CanonicalId.Mint(),
            MoAffixAllomorphPhoneEnvOperationKinds.AddRefPhoneEnv, target: stagedForm,
            after: JsonSerializer.SerializeToElement(new { member = stagedEnvironment.Value }),
            dependsOn: [new(stagedAlternateCreate.OperationId), new(stagedEnvironmentCreate.OperationId)]);
        var stagedProposal = Proposal(stagedEnvironmentOperations
            .Concat(stagedAlternateOperations).Append(environmentLink).ToArray());
        var stagedDryRun = ScratchDryRun.Of(cache, stagedProposal);
        ChangeClosedValue(cache, Get<IPhPhoneme>(cache, phoneme).FeaturesOA!, selected.Feature, otherValue);
        var beforeStagedRefusal = BaselineSemanticDigest.Compute(cache);
        Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache, stagedProposal,
            stagedDryRun.Anchor, "feature-prerequisite-tests"));
        Assert.Equal(beforeStagedRefusal, BaselineSemanticDigest.Compute(cache));
        Assert.False(Exists(cache, stagedEnvironment));
        Assert.False(Exists(cache, stagedForm));
        Assert.Empty(Get<ILexEntry>(cache, stagedEntry).AlternateFormsOS);
        var freshStagedDryRun = ScratchDryRun.Of(cache, stagedProposal);
        Assert.Equal(beforeStagedRefusal, BaselineSemanticDigest.Compute(cache));
        Assert.False(ProposalApplier.Apply(cache, stagedProposal, freshStagedDryRun.Anchor,
            "feature-prerequisite-tests").AlreadyApplied);
        AssertAlternateReadback(cache, stagedEntry, stagedForm, stagedEnvironment, "nw");
        Assert.Equal("/ [V] _", Get<IPhEnvironment>(cache, stagedEnvironment).StringRepresentation.Text);
        AssertClosedValue(Get<IPhPhoneme>(cache, phoneme).FeaturesOA!, selected.Feature, otherValue);
    }

    [Fact]
    public void AnIncumbentRestrictionDefinitionIsBoundWhenAddingAnUnrestrictedAlternate()
    {
        using var cache = pristine.NewScratch();
        var category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
        var cls = AddInflectionClass(cache, category, "one");
        var entry = Created(ApplyOps(cache, AuthorLexicalEntryComposer.Build(cache, new("m", Vern(cache),
            MorphType(MoMorphTypeTags.kguidMorphPrefix), "one", Anal(cache),
            new(LexicalEntryMsaKind.Inflectional, category), InflectionClasses: [cls]))), LexDbEntriesOperationKinds.Create);
        var proposal = Proposal(AuthorAlternateFormComposer.Build(cache,
            new(entry, "mw", Vern(cache), MorphType(MoMorphTypeTags.kguidMorphPrefix))));
        var dryRun = ScratchDryRun.Of(cache, proposal);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            Get<IMoInflClass>(cache, cls).Name.set_String(cache.DefaultAnalWs, "changed"));
        Assert.Throws<ApplyPreconditionException>(() => ProposalApplier.Apply(cache, proposal, dryRun.Anchor, "incumbent-tests"));
        Assert.Empty(Get<ILexEntry>(cache, entry).AlternateFormsOS);
    }

    private static (PhonologicalFeatureValue Selected, CanonicalId OtherValue) AddPhonologicalFeatureValues(
        LcmCache cache)
    {
        IFsClosedFeature feature = null!;
        IFsSymFeatVal selected = null!;
        IFsSymFeatVal other = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            feature = cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create();
            cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(feature);
            selected = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            other = cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
            feature.ValuesOC.Add(selected);
            feature.ValuesOC.Add(other);
        });
        return (new(CanonicalId.FromGuid(feature.Guid), CanonicalId.FromGuid(selected.Guid)),
            CanonicalId.FromGuid(other.Guid));
    }

    private static void AssertClosedValue(IFsFeatStruc structure, CanonicalId feature,
        CanonicalId value)
    {
        var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        Assert.Equal(feature.ToGuid(), specification.FeatureRA.Guid);
        Assert.Equal(value.ToGuid(), specification.ValueRA.Guid);
    }

    private static void ChangeClosedValue(LcmCache cache, IFsFeatStruc structure, CanonicalId feature,
        CanonicalId value)
    {
        var specification = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        var featureGuid = specification.FeatureRA.Guid;
        var specificationGuid = specification.Guid;
        Assert.Equal(feature.ToGuid(), featureGuid);
        var newValue = Get<IFsSymFeatVal>(cache, value);
        Assert.NotNull(newValue.Owner);
        Assert.Equal(featureGuid, newValue.Owner!.Guid);
        Assert.NotEqual(specification.ValueRA.Guid, newValue.Guid);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            specification.ValueRA = newValue);
        Assert.Equal(featureGuid, specification.FeatureRA.Guid);
        Assert.Equal(specificationGuid, specification.Guid);
        Assert.Equal(value.ToGuid(), specification.ValueRA.Guid);
    }

    private static void AssertAlternateReadback(LcmCache cache, CanonicalId entryId, CanonicalId formId,
        CanonicalId environmentId, string expectedForm)
    {
        var alternate = Assert.IsAssignableFrom<IMoAffixAllomorph>(
            Assert.Single(Get<ILexEntry>(cache, entryId).AlternateFormsOS));
        Assert.Equal(formId.ToGuid(), alternate.Guid);
        Assert.Equal(expectedForm, alternate.Form.get_String(cache.DefaultVernWs)?.Text);
        Assert.Equal(environmentId.ToGuid(), Assert.Single(alternate.PhoneEnvRC).Guid);
    }

}
