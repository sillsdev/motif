using SIL.Motif.Runner.Execution;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Model.Effects;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InflectionAuthoringTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;
    private string Ws => _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);
    private ICmObjectRepository Objects => _cache.ServiceLocator.GetInstance<ICmObjectRepository>();

    public InflectionAuthoringTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose() => _cache.Dispose();

    [Fact]
    public void ClosedFeatureValuesAndNormalizedNamesRoundTripWithExplicitDependencies()
    {
        var before = _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Count;
        var operations = AuthorInflectionFeatureComposer.Build(_cache,
            new("Numéro", Ws, [new("Singular", "sg"), new("Plural", "pl")], "num"));
        var featureId = Created(operations, FsFeatureSystemFeaturesOperationKinds.Create);
        Assert.Equal(before, _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Count);
        var proposal = Proposal(operations.Reverse().ToArray());
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.FsFeatureSystemFeatures);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.FsClosedFeatureValues);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "inflection-tests");
        var feature = Assert.IsAssignableFrom<IFsClosedFeature>(Objects.GetObject(featureId.ToGuid()));
        Assert.Equal(_cache.LangProject.MsFeatureSystemOA.Guid, feature.Owner?.Guid);
        Assert.Equal("num", feature.Abbreviation.get_String(_cache.DefaultAnalWs).Text);
        Assert.Equal(2, feature.ValuesOC.Count);
        Assert.Equal("Nume\u0301ro", FsFeatDefnAuthoringSnapshotter.Snapshot(_cache, feature)
            .AlternativesFields[SnapshotFields.FsFeatDefnName][Ws]);
        Assert.All(feature.ValuesOC, value => Assert.Equal(feature.Guid, value.Owner?.Guid));
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var stored = Assert.IsAssignableFrom<IFsClosedFeature>(reopened.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(featureId.ToGuid()));
        Assert.Equal(["Plural", "Singular"], stored.ValuesOC.Select(value => value.Name.get_String(reopened.DefaultAnalWs).Text)
            .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InflectionClassesAndSubclassesRoundTripWithoutChangingTheDefault()
    {
        var category = (IPartOfSpeech)Objects.GetObject(_category.ToGuid());
        var defaultId = category.DefaultInflectionClassRA?.Guid;
        var parent = Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Class Seven", Ws, "seven")));
        var parentId = Created(parent.Operations, PartOfSpeechInflectionClassesOperationKinds.Create);
        var child = Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Subclass", Ws, Parent: parentId)));
        var childId = Created(child.Operations, MoInflClassSubclassesOperationKinds.Create);
        Assert.Equal(defaultId, category.DefaultInflectionClassRA?.Guid);
        Assert.Equal(parentId.ToGuid(), Objects.GetObject(childId.ToGuid()).Owner?.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var classes = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
        Assert.Equal(parentId.ToGuid(), classes.GetObject(childId.ToGuid()).Owner?.Guid);
        Assert.Equal("Subclass", ((IMoInflClass)classes.GetObject(childId.ToGuid())).Name.get_String(reopened.DefaultAnalWs).Text);
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionClassComposer.Build(_cache,
            new(_category, "Subclass", Ws)));
    }

    [Fact]
    public void StemNameRegionsCarryExactFeatureChoicesAndReopenWithTheirOwner()
    {
        var (feature, value) = Feature();
        var operations = AuthorStemNameComposer.Build(_cache,
            new(_category, "Plural stem", Ws, [[new(feature, value)]], "plural"));
        var proposal = Apply(operations.Reverse().ToArray());
        var id = Created(proposal.Operations, PartOfSpeechStemNamesOperationKinds.Create);
        var name = Assert.IsAssignableFrom<IMoStemName>(Objects.GetObject(id.ToGuid()));
        Assert.Equal(_category.ToGuid(), name.Owner?.Guid);
        var region = Assert.Single(name.RegionsOC);
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(region.FeatureSpecsOC));
        Assert.Equal(feature.ToGuid(), spec.FeatureRA?.Guid);
        Assert.Equal(value.ToGuid(), spec.ValueRA?.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var stored = (IMoStemName)reopened.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id.ToGuid());
        Assert.Equal(_category.ToGuid(), stored.Owner?.Guid);
        Assert.Equal(value.ToGuid(), ((IFsClosedValue)Assert.Single(Assert.Single(stored.RegionsOC).FeatureSpecsOC)).ValueRA?.Guid);
    }

    [Fact]
    public void InvalidNamesWritingSystemsValuesAndRegionsAreRefusedBeforeMutation()
    {
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionFeatureComposer.Build(_cache, new("Empty", Ws, [])));
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionFeatureComposer.Build(_cache,
            new("Duplicate", Ws, [new("é"), new("e\u0301")])));
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionFeatureComposer.Build(_cache,
            new("Unknown", "qaa-x-nowhere", [new("Value")])));
        var (feature, value) = Feature();
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionFeatureComposer.Build(_cache,
            new("Number", Ws, [new("Other")])));
        Assert.Throws<InvalidOperationException>(() => AuthorStemNameComposer.Build(_cache, new(_category, "Empty", Ws, [])));
        Assert.Throws<InvalidOperationException>(() => AuthorStemNameComposer.Build(_cache, new(_category, "Empty region", Ws, [[]])));
        Assert.Throws<InvalidOperationException>(() => AuthorStemNameComposer.Build(_cache,
            new(_category, "Duplicate feature", Ws, [[new(feature, value), new(feature, value)]])));
        Assert.ThrowsAny<Exception>(() => AuthorStemNameComposer.Build(_cache,
            new(_category, "Wrong value", Ws, [[new(feature, _category)]])));
        var otherFeature = Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Gender", Ws, [new("Other")])));
        var foreign = Created(otherFeature.Operations, FsClosedFeatureValuesOperationKinds.Create);
        Assert.Throws<InvalidOperationException>(() => AuthorStemNameComposer.Build(_cache,
            new(_category, "Foreign value", Ws, [[new(feature, foreign)]])));
        Assert.ThrowsAny<Exception>(() => AuthorInflectionClassComposer.Build(_cache,
            new(_category, "Wrong parent", Ws, Parent: feature)));
    }

    [Theory]
    [InlineData(FsFeatureSystemFeaturesOperationKinds.Create, "FsClosedFeature")]
    [InlineData(FsClosedFeatureValuesOperationKinds.Create, "")]
    [InlineData(PartOfSpeechInflectionClassesOperationKinds.Create, "")]
    [InlineData(MoInflClassSubclassesOperationKinds.Create, "")]
    [InlineData(PartOfSpeechStemNamesOperationKinds.Create, "")]
    [InlineData(MoStemNameRegionsOperationKinds.Create, "")]
    public void EachCreateRefusesUnknownPayloadsCollisionsAndDuplicateMemberSlots(string kind, string concreteClass)
    {
        var (feature, value) = Feature();
        var parent = Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Parent", Ws)));
        var parentId = Created(parent.Operations, PartOfSpeechInflectionClassesOperationKinds.Create);
        var stem = Apply(AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(feature, value)]])));
        var stemId = Created(stem.Operations, PartOfSpeechStemNamesOperationKinds.Create);
        var stemName = (IMoStemName)Objects.GetObject(stemId.ToGuid());
        var (owner, existing) = kind switch
        {
            FsFeatureSystemFeaturesOperationKinds.Create => (CanonicalId.FromGuid(_cache.LangProject.MsFeatureSystemOA.Guid), feature),
            FsClosedFeatureValuesOperationKinds.Create => (feature, value),
            PartOfSpeechInflectionClassesOperationKinds.Create => (_category, parentId),
            MoInflClassSubclassesOperationKinds.Create => (parentId, Created(Apply(AuthorInflectionClassComposer.Build(_cache,
                new(_category, "Child", Ws, Parent: parentId))).Operations, MoInflClassSubclassesOperationKinds.Create)),
            PartOfSpeechStemNamesOperationKinds.Create => (_category, stemId),
            _ => (stemId, CanonicalId.FromGuid(Assert.Single(stemName.RegionsOC).Guid)),
        };
        var payload = concreteClass.Length == 0 ? JsonSerializer.SerializeToElement(new { }) :
            JsonSerializer.SerializeToElement(new { @class = concreteClass });
        OperationEnvelope Create(CanonicalId id, JsonElement after) => new(CanonicalId.Mint(), kind, target: owner,
            entityId: id, after: after);
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(existing, payload)])));
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        var wrong = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(_category, payload)])));
        Assert.Contains("semantic conflict", wrong.Message, StringComparison.Ordinal);
        Assert.Throws<ContractParseException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(CanonicalId.Mint(), JsonSerializer.SerializeToElement(new { unknown = "bad" }))])));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([
            new(CanonicalId.Mint(), kind, target: owner, entityId: CanonicalId.Mint(), after: payload,
                placement: new Placement(existing, null))])));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([
            new(CanonicalId.Mint(), kind, target: owner, entityId: CanonicalId.Mint(), after: payload,
                storageIdOverride: CanonicalId.Mint())])));
        if (concreteClass.Length != 0)
            Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([
                Create(CanonicalId.Mint(), JsonSerializer.SerializeToElement(new { @class = "FsOpenFeature" }))])));
        var member = CanonicalId.Mint();
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([Create(member, payload), Create(member, payload)]));
    }

    [Theory]
    [InlineData(FsFeatDefnNameOperationKinds.SetName, FsFeatDefnNameOperationKinds.ClearName)]
    [InlineData(FsSymFeatValNameOperationKinds.SetName, FsSymFeatValNameOperationKinds.ClearName)]
    [InlineData(MoInflClassNameOperationKinds.SetName, MoInflClassNameOperationKinds.ClearName)]
    [InlineData(MoStemNameNameOperationKinds.SetName, MoStemNameNameOperationKinds.ClearName)]
    public void NameWritesUseOneSlotPerWritingSystemAndClearAppearsInDiff(string setKind, string clearKind)
    {
        var (feature, value) = Feature();
        var cls = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Class", Ws))).Operations,
            PartOfSpeechInflectionClassesOperationKinds.Create);
        var stem = Created(Apply(AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(feature, value)]]))).Operations,
            PartOfSpeechStemNamesOperationKinds.Create);
        var id = setKind switch
        {
            FsFeatDefnNameOperationKinds.SetName => feature,
            FsSymFeatValNameOperationKinds.SetName => value,
            MoInflClassNameOperationKinds.SetName => cls,
            _ => stem,
        };
        var set = new OperationEnvelope(CanonicalId.Mint(), setKind, target: id,
            after: JsonSerializer.SerializeToElement(new { ws = Ws, text = "Changed" }));
        var clear = new OperationEnvelope(CanonicalId.Mint(), clearKind, target: id,
            after: JsonSerializer.SerializeToElement(new { ws = Ws }));
        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([set, clear]));
        var other = new OperationEnvelope(CanonicalId.Mint(), setKind, target: id,
            after: JsonSerializer.SerializeToElement(new { ws = "fr", text = "Other" }));
        Assert.Null(ProposalOperationSlotValidator.FindConflict([set, other]));
        var before = OperationHandlerRegistry.Resolve(clearKind, "inflection-tests").ReadCurrentFootprint(_cache, clear);
        Apply([clear]);
        var after = OperationHandlerRegistry.Resolve(clearKind, "inflection-tests").ReadCurrentFootprint(_cache, clear);
        Assert.NotEmpty(before.Before);
        Assert.Empty(after.After);
        IReadOnlyDictionary<CanonicalId, ObjectSnapshot> Snapshot(ExpectedEffect effect) =>
            new Dictionary<CanonicalId, ObjectSnapshot>
            { [id] = new(id, new Dictionary<string, IReadOnlyDictionary<string, string>> { [effect.Field] = effect.Before }) };
        Assert.Equal(before.Field, Assert.Single(SoundSystemSnapshots.Compare(Snapshot(before), Snapshot(after))).Field);
    }

    [Theory]
    [InlineData(FsFeatDefnNameOperationKinds.SetName)]
    [InlineData(FsSymFeatValNameOperationKinds.SetName)]
    [InlineData(MoInflClassNameOperationKinds.SetName)]
    [InlineData(MoStemNameNameOperationKinds.SetName)]
    public void ChangedNamesRequireFreshEvidenceWithoutChangingAuthoredIntent(string kind)
    {
        var (feature, value) = Feature();
        var cls = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Class", Ws))).Operations,
            PartOfSpeechInflectionClassesOperationKinds.Create);
        var stem = Created(Apply(AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(feature, value)]]))).Operations,
            PartOfSpeechStemNamesOperationKinds.Create);
        var id = kind switch
        {
            FsFeatDefnNameOperationKinds.SetName => feature,
            FsSymFeatValNameOperationKinds.SetName => value,
            MoInflClassNameOperationKinds.SetName => cls,
            _ => stem,
        };
        OperationEnvelope Rename(string text) => new(CanonicalId.Mint(), kind, target: id,
            after: JsonSerializer.SerializeToElement(new { ws = Ws, text }));
        var proposal = Proposal([Rename("Authored")]);
        var evidence = ScratchDryRun.Of(_cache, proposal);
        Apply([Rename("Concurrent")]);
        Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, evidence.Anchor, "inflection-tests"));
        var current = OperationHandlerRegistry.Resolve(kind, "inflection-tests")
            .ReadCurrentFootprint(_cache, proposal.Operations[0]);
        Assert.Equal("Concurrent", current.Before[Ws]);
        var digest = SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "inflection-tests");
        Assert.Equal(digest, SIL.Motif.Contract.Canonicalization.IntentDigest.Compute(proposal));
        Assert.Equal("Authored", OperationHandlerRegistry.Resolve(kind, "inflection-tests")
            .ReadCurrentFootprint(_cache, proposal.Operations[0]).Before[Ws]);
    }

    [Fact]
    public void FailureMidSubclassRollsBackItsWholeGraph()
    {
        var parent = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Parent", Ws))).Operations,
            PartOfSpeechInflectionClassesOperationKinds.Create);
        var operations = AuthorInflectionClassComposer.Build(_cache, new(_category, "Rollback child", Ws, "child", parent));
        var child = Created(operations, MoInflClassSubclassesOperationKinds.Create);
        var proposal = Proposal(operations);
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal,
            ScratchDryRun.Of(_cache, proposal).Anchor, "inflection-tests", "", (_, op) =>
            { if (op.Kind == MoInflClassAbbreviationOperationKinds.SetAbbreviation) throw new InvalidOperationException("injected"); }));
        Assert.False(Objects.IsValidObjectId(child.ToGuid()));
        Assert.Empty(((IMoInflClass)Objects.GetObject(parent.ToGuid())).SubclassesOC);
    }

    [Fact]
    public void ForeignClassParentsAndPhonologicalFeaturesAreRefused()
    {
        var other = Created(Apply(AuthorPartOfSpeechComposer.Build(_cache, new("Other category", Ws))).Operations,
            CmPossibilityListPossibilitiesOperationKinds.Create);
        var cls = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(other, "Foreign class", Ws))).Operations,
            PartOfSpeechInflectionClassesOperationKinds.Create);
        Assert.Throws<InvalidOperationException>(() => AuthorInflectionClassComposer.Build(_cache,
            new(_category, "Child", Ws, Parent: cls)));
        var (feature, value) = Feature();
        var obj = (IFsClosedFeature)Objects.GetObject(feature.ToGuid());
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(obj));
        Assert.Throws<InvalidOperationException>(() => AuthorStemNameComposer.Build(_cache,
            new(_category, "Wrong system", Ws, [[new(feature, value)]])));
    }

    [Fact]
    public void InflectionGraphDiffIncludesEveryCreateAndItsExactOwningMembership()
    {
        var before = InflectionSnapshots.Read(_cache);
        var feature = Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Number", Ws, [new("Plural", "pl")])));
        var featureId = Created(feature.Operations, FsFeatureSystemFeaturesOperationKinds.Create);
        var valueId = Created(feature.Operations, FsClosedFeatureValuesOperationKinds.Create);
        var parent = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Class", Ws))).Operations,
            PartOfSpeechInflectionClassesOperationKinds.Create);
        var child = Created(Apply(AuthorInflectionClassComposer.Build(_cache, new(_category, "Subclass", Ws, Parent: parent))).Operations,
            MoInflClassSubclassesOperationKinds.Create);
        var stem = Apply(AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(featureId, valueId)]])));
        var after = InflectionSnapshots.Read(_cache);
        var effects = SoundSystemSnapshots.Compare(before, after);
        foreach (var field in new[] { SnapshotFields.FsFeatureSystemFeatures, SnapshotFields.FsClosedFeatureValues,
                     SnapshotFields.PartOfSpeechInflectionClasses, SnapshotFields.MoInflClassSubclasses,
                     SnapshotFields.PartOfSpeechStemNames, SnapshotFields.MoStemNameRegions, SnapshotFields.FsFeatStrucFeatureSpecs,
                     SnapshotFields.FsFeatureSpecificationFeature, SnapshotFields.FsClosedValueValue,
                     SnapshotFields.FsFeatDefnName, SnapshotFields.FsSymFeatValName, SnapshotFields.MoInflClassName, SnapshotFields.MoStemNameName })
            Assert.Contains(effects, effect => effect.Field == field);
        var childSnapshot = after[child];
        Assert.Equal(parent.Value, childSnapshot.AlternativesFields["owner"]["ref"]);
        foreach (var create in feature.Operations.Concat(stem.Operations).Where(op => op.EntityId is not null))
            Assert.Contains(create.EntityId!.Value, after.Keys);
        Assert.Equal(new[] { valueId.Value }, after[featureId].AlternativesFields[SnapshotFields.FsClosedFeatureValues].Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARegionRefusesChoicesThatChangedOwnershipAfterItsDryRun(bool moveFeature)
    {
        var (feature, value) = Feature();
        var other = Created(Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Gender", Ws, [new("Common")]))).Operations,
            FsFeatureSystemFeaturesOperationKinds.Create);
        var operations = AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(feature, value)]]));
        var proposal = Proposal(operations);
        var evidence = ScratchDryRun.Of(_cache, proposal);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            if (moveFeature) _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add((IFsClosedFeature)Objects.GetObject(feature.ToGuid()));
            else ((IFsClosedFeature)Objects.GetObject(other.ToGuid())).ValuesOC.Add((IFsSymFeatVal)Objects.GetObject(value.ToGuid()));
        });
        var refusal = Assert.Throws<PreconditionRefusal>(() => ProposalApplier.Apply(_cache, proposal, evidence.Anchor, "inflection-tests"));
        Assert.Contains("Footprint drift", refusal.Message, StringComparison.Ordinal);
        Assert.All(operations.Where(op => op.EntityId is not null), op => Assert.False(Objects.IsValidObjectId(op.EntityId!.Value.ToGuid())));
    }

    [Fact]
    public void DryRunAlsoRefusesAValueMovedAfterComposition()
    {
        var (feature, value) = Feature();
        var other = Created(Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Gender", Ws, [new("Common")]))).Operations,
            FsFeatureSystemFeaturesOperationKinds.Create);
        var operations = AuthorStemNameComposer.Build(_cache, new(_category, "Stem", Ws, [[new(feature, value)]]));
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            ((IFsClosedFeature)Objects.GetObject(other.ToGuid())).ValuesOC.Add((IFsSymFeatVal)Objects.GetObject(value.ToGuid())));
        var refusal = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(operations)));
        Assert.Contains("morphosyntactic feature", refusal.Message, StringComparison.Ordinal);
        Assert.All(operations.Where(op => op.EntityId is not null), op => Assert.False(Objects.IsValidObjectId(op.EntityId!.Value.ToGuid())));
    }

    [Fact]
    public void MovingAValueBetweenFeaturesChangesTheBaselineDigest()
    {
        var (feature, value) = Feature();
        var other = Created(Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Gender", Ws, [new("Common")]))).Operations,
            FsFeatureSystemFeaturesOperationKinds.Create);
        var before = SIL.Motif.LiveHost.Baselines.BaselineSemanticDigest.Compute(_cache);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            ((IFsClosedFeature)Objects.GetObject(other.ToGuid())).ValuesOC.Add((IFsSymFeatVal)Objects.GetObject(value.ToGuid())));
        Assert.NotEqual(before, SIL.Motif.LiveHost.Baselines.BaselineSemanticDigest.Compute(_cache));
        Assert.DoesNotContain(((IFsClosedFeature)Objects.GetObject(feature.ToGuid())).ValuesOC, item => item.Guid == value.ToGuid());
    }

    [Fact]
    public void FailureMidFeatureRollsBackEveryCreatedMember()
    {
        var featureOps = AuthorInflectionFeatureComposer.Build(_cache, new("Rollback", Ws, [new("Value")]));
        var featureId = Created(featureOps, FsFeatureSystemFeaturesOperationKinds.Create);
        var featureProposal = Proposal(featureOps);
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, featureProposal,
            ScratchDryRun.Of(_cache, featureProposal).Anchor, "inflection-tests", "", (_, op) =>
            { if (op.Kind == FsSymFeatValNameOperationKinds.SetName) throw new InvalidOperationException("injected"); }));
        Assert.False(Objects.IsValidObjectId(featureId.ToGuid()));
        Assert.All(featureOps.Where(op => op.EntityId is not null), op => Assert.False(Objects.IsValidObjectId(op.EntityId!.Value.ToGuid())));
    }

    [Fact]
    public void FailureMidRegionRollsBackTheStemNameAndItsSpecifications()
    {
        var (feature, value) = Feature();
        var nameOps = AuthorStemNameComposer.Build(_cache, new(_category, "Rollback stem", Ws, [[new(feature, value)]]));
        var stemId = Created(nameOps, PartOfSpeechStemNamesOperationKinds.Create);
        var proposal = Proposal(nameOps);
        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal,
            ScratchDryRun.Of(_cache, proposal).Anchor, "inflection-tests", "", (_, op) =>
            { if (op.Kind == FsClosedValueValueOperationKinds.SetValue) throw new InvalidOperationException("injected"); }));
        Assert.False(Objects.IsValidObjectId(stemId.ToGuid()));
        Assert.All(nameOps.Where(op => op.EntityId is not null), op => Assert.False(Objects.IsValidObjectId(op.EntityId!.Value.ToGuid())));
    }

    [Theory]
    [InlineData("feature", "{\"name\":\"X\",\"ws\":\"en\",\"values\":[{\"name\":\"Y\",\"extra\":true}]}")]
    [InlineData("class", "{\"category\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"name\":\"X\",\"ws\":\"en\",\"extra\":true}")]
    [InlineData("stem", "{\"category\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"name\":\"X\",\"ws\":\"en\",\"regions\":[[{\"feature\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"value\":\"AAAAAAAAAAAAAAAAAAAAAA\",\"extra\":true}]]}")]
    public void IntentParsersRejectUnknownPropertiesAtEveryDepth(string parser, string json)
    {
        using var input = JsonDocument.Parse(json);
        Assert.Throws<ContractParseException>(() =>
        {
            if (parser == "feature") AuthorInflectionFeatureIntentParser.Parse(input.RootElement);
            else if (parser == "class") AuthorInflectionClassIntentParser.Parse(input.RootElement);
            else AuthorStemNameIntentParser.Parse(input.RootElement);
        });
    }

    private (CanonicalId Feature, CanonicalId Value) Feature()
    {
        var proposal = Apply(AuthorInflectionFeatureComposer.Build(_cache, new("Number", Ws, [new("Plural", "pl")], "num")));
        return (Created(proposal.Operations, FsFeatureSystemFeaturesOperationKinds.Create),
            Created(proposal.Operations, FsClosedFeatureValuesOperationKinds.Create));
    }

    private Proposal Apply(IReadOnlyList<OperationEnvelope> operations)
    {
        var proposal = Proposal(operations);
        ProposalApplier.Apply(_cache, proposal, ScratchDryRun.Of(_cache, proposal).Anchor, "inflection-tests");
        return proposal;
    }

    private static CanonicalId Created(IReadOnlyList<OperationEnvelope> operations, string kind) =>
        Assert.Single(operations, op => op.Kind == kind).EntityId!.Value;

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
