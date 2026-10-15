using SIL.Motif.Runner.Execution;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.InflFeatsTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InflFeatsSafetyTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public InflFeatsSafetyTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void ComposerChoosesInflectionalOwningSlotAndRefusesBeforeMintingWhenOccupied()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var operation = Assert.Single(AuthorFeatureStructureComposer.Build(_cache, new(msa)));
        Assert.Equal(MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats, operation.Kind);
        ApplyOps(_cache, [operation]);
        var minted = 0;
        Assert.Throws<InvalidOperationException>(() => AuthorFeatureStructureComposer.Build(_cache, new(msa), () =>
        {
            minted++;
            return CanonicalId.Mint();
        }));
        Assert.Equal(0, minted);
        Assert.Equal(operation.EntityId!.Value.ToGuid(), Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid);
    }

    [Theory]
    [InlineData("phonological")]
    [InlineData("foreign-value")]
    [InlineData("foreign-type")]
    [InlineData("type-membership")]
    [InlineData("duplicate-feature")]
    [InlineData("value-without-feature")]
    public void CompleteRawGraphRefusesInvalidInflFeatsAndLeavesLiveGraphUnchanged(string scenario)
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var (_, otherValues) = AddFeature(_cache, "other", "other-value");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var nativeStructure = Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            if (scenario == "phonological")
                _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature));
            if (scenario is "foreign-type" or "type-membership")
            {
                var type = _cache.ServiceLocator.GetInstance<IFsFeatStrucTypeFactory>().Create();
                (scenario == "foreign-type" ? _cache.LangProject.PhFeatureSystemOA : _cache.LangProject.MsFeatureSystemOA)
                    .TypesOC.Add(type);
                if (scenario == "foreign-type") type.FeaturesRS.Add(Get<IFsClosedFeature>(_cache, feature));
                nativeStructure.TypeRA = type;
            }
        });
        var selection = Selection(create.EntityId!.Value,
            scenario == "value-without-feature" ? null : feature,
            scenario == "foreign-value" ? otherValues[0] : values[0]);
        if (scenario == "duplicate-feature")
            selection = selection.Concat(Selection(create.EntityId.Value, feature, values[0])).ToArray();
        var proposal = Proposal(selection);
        new FwDataProjectLoader().Save(_cache);
        var before = BaselineSemanticDigest.Compute(_cache);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Empty(nativeStructure.FeatureSpecsOC);
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
    }

    [Fact]
    public void ComposerRefusesPhonologicalFeatureOnlyOnInflFeats()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var (feature, _) = AddFeature(_cache, "voice", "voiced");
        IPhPhoneme phoneme = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature));
            var set = _cache.ServiceLocator.GetInstance<IPhPhonemeSetFactory>().Create();
            _cache.LangProject.PhonologicalDataOA.PhonemeSetsOS.Add(set);
            phoneme = _cache.ServiceLocator.GetInstance<IPhPhonemeFactory>().Create();
            set.PhonemesOC.Add(phoneme);
            phoneme.FeaturesOA = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
        });
        Assert.Throws<InvalidOperationException>(() => AuthorFeatureValueComposer.Build(_cache,
            new(create.EntityId!.Value, feature)));
        var phonological = AuthorFeatureValueComposer.Build(_cache, new(Id(phoneme.FeaturesOA), feature));
        ApplyOps(_cache, phonological);
        Assert.Equal(feature.ToGuid(), Assert.Single(phoneme.FeaturesOA.FeatureSpecsOC).FeatureRA.Guid);
    }

    [Fact]
    public void FeatureOnlySelectionRemainsValidAndPersistsExactSpecWithoutAValue()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var create = Structure(msa);
        var (feature, _) = AddFeature(_cache, "nc", "seven");
        var selection = Selection(create.EntityId!.Value, feature, structureCreator: create.OperationId);
        var proposal = Proposal(selection.Prepend(create).Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.Equal(3, dryRun.ExpectedEffects.Count);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "feature-only-tests");
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var structure = Get<IMoInflAffMsa>(reopened, msa).InflFeatsOA!;
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        Assert.Equal(create.EntityId.Value.ToGuid(), structure.Guid);
        Assert.Equal(selection[0].EntityId!.Value.ToGuid(), spec.Guid);
        Assert.Equal(feature.ToGuid(), spec.FeatureRA.Guid);
        Assert.Null(spec.ValueRA);
    }

    [Theory]
    [InlineData("feature-owner", false)]
    [InlineData("value-owner", false)]
    [InlineData("msa-owner", false)]
    [InlineData("structure-owner", false)]
    [InlineData("category", false)]
    [InlineData("type-membership", false)]
    [InlineData("type-owner", false)]
    [InlineData("definition", false)]
    [InlineData("feature-removed", false)]
    [InlineData("collision", false)]
    [InlineData("feature-owner", true)]
    [InlineData("value-owner", true)]
    [InlineData("category", true)]
    [InlineData("collision", true)]
    public void ReadonlyPrerequisiteDriftRefusesApplyBeforeMutation(string scenario, bool stagedStructure)
    {
        var (entry, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (_, otherMsa, _) = AddInflectionalPrefix(_cache, "vi", _category);
        var otherEntry = AddStem(_cache, "root", _category).Entry;
        var otherCategory = AddCategory(_cache, "other-category");
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var (otherFeature, _) = AddFeature(_cache, "other", "other-value");
        var create = Structure(msa);
        if (!stagedStructure) ApplyOps(_cache, [create]);
        IFsFeatStrucType type = null!;
        if (!stagedStructure)
            NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            {
                type = _cache.ServiceLocator.GetInstance<IFsFeatStrucTypeFactory>().Create();
                _cache.LangProject.MsFeatureSystemOA.TypesOC.Add(type);
                type.FeaturesRS.Add(Get<IFsClosedFeature>(_cache, feature));
                Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.TypeRA = type;
            });
        var selection = Selection(create.EntityId!.Value, feature, values[0], stagedStructure ? create.OperationId : null);
        var proposal = Proposal(stagedStructure ? selection.Prepend(create).ToArray() : selection);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect => effect.CanonicalId == feature || effect.CanonicalId == values[0]);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            switch (scenario)
            {
                case "feature-owner": _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature)); break;
                case "value-owner": Get<IFsClosedFeature>(_cache, otherFeature).ValuesOC.Add(Get<IFsSymFeatVal>(_cache, values[0])); break;
                case "msa-owner": Get<ILexEntry>(_cache, otherEntry).MorphoSyntaxAnalysesOC.Add(Get<IMoInflAffMsa>(_cache, msa)); break;
                case "structure-owner": Get<IMoInflAffMsa>(_cache, otherMsa).InflFeatsOA = Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA; break;
                case "category": Get<IMoInflAffMsa>(_cache, msa).PartOfSpeechRA = Get<IPartOfSpeech>(_cache, otherCategory); break;
                case "type-membership": type.FeaturesRS.Clear(); break;
                case "type-owner": _cache.LangProject.PhFeatureSystemOA.TypesOC.Add(type); break;
                case "definition": Get<IFsClosedFeature>(_cache, feature).Abbreviation.set_String(_cache.DefaultAnalWs, "changed"); break;
                case "feature-removed": Get<IFsClosedFeature>(_cache, feature).Delete(); break;
                case "collision":
                    var colliding = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create(selection[0].EntityId!.Value.ToGuid());
                    Get<IFsClosedFeature>(_cache, otherFeature).DefaultOA = colliding;
                    break;
            }
        });
        if (scenario == "category" && !stagedStructure)
            Assert.False(Exists(_cache, create.EntityId!.Value));
        var before = BaselineSemanticDigest.Compute(_cache);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "feature-drift-tests"));
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        if (scenario != "collision") Assert.False(Exists(_cache, selection[0].EntityId!.Value));
        if (stagedStructure) Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.True(Exists(_cache, entry));
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), applied => applied.ProposalId == proposal.ProposalId.ToGuid());
    }

    [Fact]
    public void UnrelatedLexicalEditSurvivesFeatureApply()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var create = Structure(msa);
        var selection = Selection(create.EntityId!.Value, feature, values[0], create.OperationId);
        var proposal = Proposal(selection.Prepend(create).ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var (otherEntry, _, _) = AddStem(_cache, "unrelated", _category);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "feature-unrelated-tests");
        Assert.True(Exists(_cache, otherEntry));
        Assert.Equal(values[0].ToGuid(), Assert.IsAssignableFrom<IFsClosedValue>(
            Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC)).ValueRA.Guid);
    }

    [Fact]
    public void DuplicateRawReferenceIsRefusedAtDryRunEntrypoint()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, _) = AddFeature(_cache, "nc", "seven");
        var create = Structure(msa);
        var selection = Selection(create.EntityId!.Value, feature, structureCreator: create.OperationId);
        using var payload = JsonDocument.Parse($$"""{"ref":"{{feature.Value}}","ref":"{{feature.Value}}"}""");
        var duplicate = new OperationEnvelope(selection[1].OperationId, selection[1].Kind,
            target: selection[1].Target, after: payload.RootElement.Clone(), dependsOn: selection[1].DependsOn);
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([create, selection[0], duplicate])));
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
    }
    [Theory]
    [InlineData("system-replaced")]
    [InlineData("category-definition")]
    [InlineData("feature-collision")]
    [InlineData("structure-collision")]
    public void AllStagedFeatureTargetsStillBindExistingExternalAnchorsAndCollisions(string scenario)
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var featureOps = AuthorInflectionFeatureComposer.Build(_cache, new("staged-nc", Anal(_cache), [new("seven")]));
        var featureCreate = featureOps.Single(operation => operation.Kind == FsFeatureSystemFeaturesOperationKinds.Create);
        var valueCreate = featureOps.Single(operation => operation.Kind == FsClosedFeatureValuesOperationKinds.Create);
        var msaCreate = new OperationEnvelope(CanonicalId.Mint(), LexEntryMorphoSyntaxAnalysesOperationKinds.Create,
            target: entry, entityId: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { @class = "MoInflAffMsa" }));
        var categoryLink = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaPartOfSpeechOperationKinds.SetPartOfSpeech,
            target: msaCreate.EntityId, after: JsonSerializer.SerializeToElement(new { @ref = _category.Value }),
            dependsOn: [new(msaCreate.OperationId)]);
        var structure = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
            target: msaCreate.EntityId, entityId: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new { }),
            dependsOn: [new(msaCreate.OperationId)]);
        var selection = Selection(structure.EntityId!.Value, featureCreate.EntityId, valueCreate.EntityId, structure.OperationId);
        var featureLink = new OperationEnvelope(selection[1].OperationId, selection[1].Kind,
            target: selection[1].Target, after: selection[1].After,
            dependsOn: [new(selection[0].OperationId), new(featureCreate.OperationId)]);
        var valueLink = new OperationEnvelope(selection[2].OperationId, selection[2].Kind,
            target: selection[2].Target, after: selection[2].After,
            dependsOn: [new(selection[0].OperationId), new(valueCreate.OperationId)]);
        var proposal = Proposal(featureOps.Concat([msaCreate, categoryLink, structure, selection[0], featureLink, valueLink]).Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var collisionMsa = scenario == "structure-collision" ? AddInflectionalPrefix(_cache, "other", _category).Msa : default;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            switch (scenario)
            {
                case "system-replaced":
                    _cache.LangProject.MsFeatureSystemOA = _cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
                    break;
                case "category-definition":
                    Get<IPartOfSpeech>(_cache, _category).Abbreviation.set_String(_cache.DefaultAnalWs, "changed");
                    break;
                case "feature-collision":
                    var collision = _cache.ServiceLocator.GetInstance<IFsClosedFeatureFactory>().Create(featureCreate.EntityId!.Value.ToGuid());
                    _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(collision);
                    break;
                case "structure-collision":
                    MoInflAffMsaInflFeatsCreateLowering.Apply(_cache, Get<IMoInflAffMsa>(_cache, collisionMsa), structure.EntityId.Value.ToGuid());
                    break;
            }
        });
        var before = BaselineSemanticDigest.Compute(_cache);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "staged-feature-tests"));
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        Assert.False(Exists(_cache, msaCreate.EntityId!.Value));
        Assert.False(Exists(_cache, selection[0].EntityId!.Value));
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), applied => applied.ProposalId == proposal.ProposalId.ToGuid());
    }

    [Theory]
    [InlineData("structure")]
    [InlineData("spec")]
    [InlineData("value")]
    [InlineData("final-validation")]
    public void FailureAfterCreationOrSelectionRollsBackOneCompleteProposal(string stage)
    {
        var (entry, msa, form) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var create = Structure(msa);
        var selection = Selection(create.EntityId!.Value, feature, values[0], create.OperationId);
        var proposal = Proposal(selection.Prepend(create).ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var before = BaselineSemanticDigest.Compute(_cache);
        var failKind = stage switch
        {
            "structure" => create.Kind,
            "spec" => selection[0].Kind,
            _ => FsClosedValueValueOperationKinds.SetValue,
        };
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "feature-rollback-tests", string.Empty, (_, operation) =>
            {
                if (operation.Kind != failKind) return;
                if (stage == "final-validation")
                    _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature));
                else throw new InvalidOperationException("injected failure after selected write");
            }));
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.False(Exists(_cache, create.EntityId.Value));
        Assert.False(Exists(_cache, selection[0].EntityId!.Value));
        Assert.True(Exists(_cache, entry));
        Assert.Equal(form.ToGuid(), Get<ILexEntry>(_cache, entry).LexemeFormOA.Guid);
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), applied => applied.ProposalId == proposal.ProposalId.ToGuid());
    }

    [Fact]
    public void CompositionToDryRunFeatureDomainDriftRefusesTheCompletedGraph()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var operations = AuthorFeatureValueComposer.Build(_cache, new(create.EntityId!.Value, feature, values[0]));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature)));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
        Assert.Empty(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC);
    }

    [Fact]
    public void EquivalentStagedTargetSpellingPreservesAuthoredIdentityAndDeclaredDependencies()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, _) = AddFeature(_cache, "nc", "seven");
        var structureId = CanonicalId.Mint("structure:");
        var create = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
            target: msa, entityId: structureId, after: JsonSerializer.SerializeToElement(new { }));
        var selection = Selection(CanonicalId.FromGuid(structureId.ToGuid()), feature, structureCreator: create.OperationId);
        var proposal = Proposal(selection.Prepend(create).Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "feature-identity-tests");
        Assert.Equal(structureId.ToGuid(), Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid);
        Assert.True(structureId.SpellingEquals(create.EntityId!.Value));
    }

    [Fact]
    public void CompositionToDryRunValidOwnerMovePreservesTheExactStructureTarget()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (_, otherMsa, _) = AddInflectionalPrefix(_cache, "vi", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var structure = Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!;
        var operations = AuthorFeatureValueComposer.Build(_cache, new(Id(structure), feature, values[0]));
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<IMoInflAffMsa>(_cache, otherMsa).InflFeatsOA = structure);
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Empty(structure.FeatureSpecsOC);
        Assert.All(operations.Where(operation => operation.EntityId is not null),
            operation => Assert.Equal(Id(structure), operation.Target));
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "feature-owner-move-tests");
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.Equal(create.EntityId!.Value.ToGuid(), Get<IMoInflAffMsa>(_cache, otherMsa).InflFeatsOA!.Guid);
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(structure.FeatureSpecsOC));
        Assert.Equal(feature.ToGuid(), spec.FeatureRA.Guid);
        Assert.Equal(values[0].ToGuid(), spec.ValueRA.Guid);
    }

    [Fact]
    public void PriorProjectionEvidenceRefusesFeatureApplyAndFreshDryRunReplacesIt()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var create = Structure(msa);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var selection = Selection(create.EntityId!.Value, feature, values[0], create.OperationId);
        var proposal = Proposal(selection.Prepend(create).ToArray());
        var evaluated = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("8", evaluated.Anchor.ProjectionVersion);
        var obsolete = evaluated.Anchor with { ProjectionVersion = "7" };
        var before = BaselineSemanticDigest.Compute(_cache);
        var refusal = Assert.Throws<PreconditionRefusal>(() =>
            ProposalApplier.Apply(_cache, proposal, obsolete, "feature-projection-tests"));
        Assert.Contains("projection version 7", refusal.Message);
        Assert.Contains("version 8", refusal.Message);
        Assert.Contains("fresh Dry Run", refusal.Message);
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.False(Exists(_cache, selection[0].EntityId!.Value));
        Assert.DoesNotContain(ProjectAppliedLog.ReadAll(_cache), applied => applied.ProposalId == proposal.ProposalId.ToGuid());
        var recaptured = ScratchDryRun.Of(_cache, proposal);
        Assert.Equal("8", recaptured.Anchor.ProjectionVersion);
        ProposalApplier.Apply(_cache, proposal, recaptured.Anchor, "feature-projection-tests");
        Assert.Equal(create.EntityId.Value.ToGuid(), Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid);
        Assert.Equal(selection[0].EntityId!.Value.ToGuid(), Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC).Guid);
    }

}
