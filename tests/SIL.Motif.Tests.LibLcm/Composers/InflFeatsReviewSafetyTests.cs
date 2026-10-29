using SIL.Motif.Runner.Execution;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.InflFeatsTestSupport;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group2)]
public sealed class InflFeatsReviewSafetyTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public InflFeatsReviewSafetyTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose() => _cache.Dispose();

    [Theory]
    [InlineData("value-added", false)]
    [InlineData("value-removed", false)]
    [InlineData("value-abbreviation", false)]
    [InlineData("value-name", false)]
    [InlineData("value-added", true)]
    [InlineData("value-removed", true)]
    [InlineData("value-abbreviation", true)]
    [InlineData("value-name", true)]
    public void FeatureOnlySelectionBindsTheWholeClosedDomainUntilFreshDryRun(string drift, bool stagedStructure)
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven", "eight");
        var create = Structure(msa);
        if (!stagedStructure) ApplyOps(_cache, [create]);
        var selection = stagedStructure
            ? Selection(create.EntityId!.Value, feature, structureCreator: create.OperationId)
            : AuthorFeatureValueComposer.Build(_cache, new(create.EntityId!.Value, feature)).ToArray();
        var proposal = Proposal(stagedStructure ? selection.Prepend(create).ToArray() : selection);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.DoesNotContain(dryRun.ExpectedEffects, effect =>
            effect.CanonicalId == feature || values.Contains(effect.CanonicalId));
        var domain = Get<IFsClosedFeature>(_cache, feature);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            switch (drift)
            {
                case "value-added":
                    var added = _cache.ServiceLocator.GetInstance<IFsSymFeatValFactory>().Create();
                    domain.ValuesOC.Add(added);
                    added.Name.set_String(_cache.DefaultAnalWs, "nine");
                    added.Abbreviation.set_String(_cache.DefaultAnalWs, "nine");
                    break;
                case "value-removed": Get<IFsSymFeatVal>(_cache, values[1]).Delete(); break;
                case "value-abbreviation":
                    Get<IFsSymFeatVal>(_cache, values[1]).Abbreviation.set_String(_cache.DefaultAnalWs, "changed");
                    break;
                case "value-name":
                    Get<IFsSymFeatVal>(_cache, values[1]).Name.set_String(_cache.DefaultAnalWs, "changed");
                    break;
            }
        });
        var postDriftDigest = BaselineSemanticDigest.Compute(_cache);
        var postDriftMembers = domain.ValuesOC.Select(value => value.Guid).Order().ToArray();
        var appliedBefore = ProjectAppliedLog.ReadAll(_cache).ToArray();
        Receipt? receipt = null;
        Assert.Throws<PreconditionRefusal>(() =>
        {
            receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "closed-domain-tests");
        });
        Assert.Null(receipt);
        Assert.Equal(postDriftDigest, BaselineSemanticDigest.Compute(_cache));
        Assert.Equal(postDriftMembers, domain.ValuesOC.Select(value => value.Guid).Order().ToArray());
        Assert.Equal(appliedBefore, ProjectAppliedLog.ReadAll(_cache).ToArray());
        Assert.False(Exists(_cache, selection[0].EntityId!.Value));
        if (stagedStructure) Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        else Assert.Empty(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC);

        var fresh = ScratchDryRun.Of(_cache, proposal);
        var applied = ProposalApplier.Apply(_cache, proposal, fresh.Anchor, "closed-domain-tests");
        Assert.False(applied.AlreadyApplied);
        Assert.DoesNotContain(applied.ActualEffects, effect =>
            effect.CanonicalId == feature || domain.ValuesOC.Any(value => value.Guid == effect.CanonicalId.ToGuid()));
        Assert.Equal(postDriftMembers, domain.ValuesOC.Select(value => value.Guid).Order().ToArray());
        var spec = Assert.IsAssignableFrom<IFsClosedValue>(
            Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC));
        Assert.Equal(selection[0].EntityId!.Value.ToGuid(), spec.Guid);
        Assert.Equal(feature.ToGuid(), spec.FeatureRA.Guid);
        Assert.Null(spec.ValueRA);
        Assert.Single(ProjectAppliedLog.ReadAll(_cache), entry => entry.ProposalId == proposal.ProposalId.ToGuid());
    }

    [Theory]
    [InlineData("category-abbreviation")]
    [InlineData("category-name")]
    [InlineData("entry-citation-form")]
    public void UnrelatedCopyEditPreservesNativeComplexInflectionalRequirements(string edit)
    {
        var (entry, msa, form) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var incumbent = AddNativeComplexRequirement(msa, feature, values[0]);
        var operation = edit switch
        {
            "category-abbreviation" => Text(CmPossibilityAbbreviationOperationKinds.SetAbbreviation, _category),
            "category-name" => Text(CmPossibilityNameOperationKinds.SetName, _category),
            _ => Text(LexEntryCitationFormOperationKinds.SetCitationForm, entry),
        };
        var proposal = Proposal([operation]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        AssertNativeComplexRequirement(_cache, msa, incumbent, feature, values[0]);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "native-complex-tests");
        Assert.Single(receipt.ActualEffects);
        Assert.Equal(operation.Target!.Value, receipt.ActualEffects[0].CanonicalId);
        var editedText = edit switch
        {
            "category-abbreviation" => Get<IPartOfSpeech>(_cache, _category).Abbreviation.get_String(_cache.DefaultAnalWs).Text,
            "category-name" => Get<IPartOfSpeech>(_cache, _category).Name.get_String(_cache.DefaultAnalWs).Text,
            _ => Get<ILexEntry>(_cache, entry).CitationForm.get_String(_cache.DefaultAnalWs).Text,
        };
        Assert.Equal("changed", editedText);
        Assert.Equal(form.ToGuid(), Get<ILexEntry>(_cache, entry).LexemeFormOA.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        AssertNativeComplexRequirement(reopened, msa, incumbent, feature, values[0]);
    }

    [Theory]
    [InlineData("create-selection")]
    [InlineData("set-value")]
    [InlineData("clear-value")]
    public void ClosedSelectionEditPreservesTheNativeComplexSibling(string edit)
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (nestedFeature, nestedValues) = AddFeature(_cache, "nc", "seven");
        var (feature, values) = AddFeature(_cache, "number", "singular", "plural");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        var selection = Selection(create.EntityId!.Value, feature, values[0]);
        if (edit != "create-selection") ApplyOps(_cache, selection);
        var incumbent = AddNativeComplexRequirement(msa, nestedFeature, nestedValues[0]);
        IReadOnlyList<OperationEnvelope> operations = edit switch
        {
            "create-selection" => AuthorFeatureValueComposer.Build(_cache, new(create.EntityId.Value, feature, values[1])),
            "set-value" => new[] { Reference(FsClosedValueValueOperationKinds.SetValue, selection[0].EntityId!.Value, values[1]) },
            _ => new[] { new OperationEnvelope(CanonicalId.Mint(), FsClosedValueValueOperationKinds.ClearValue,
                target: selection[0].EntityId, after: JsonSerializer.SerializeToElement(new { })) },
        };
        var proposal = Proposal(operations);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        AssertNativeComplexRequirement(_cache, msa, incumbent, nestedFeature, nestedValues[0]);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "native-complex-tests");
        var closed = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(
            Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC, spec => spec.FeatureRA.Guid == feature.ToGuid()));
        Assert.Equal(feature.ToGuid(), closed.FeatureRA.Guid);
        if (edit == "clear-value") Assert.Null(closed.ValueRA);
        else Assert.Equal(values[1].ToGuid(), closed.ValueRA.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        AssertNativeComplexRequirement(reopened, msa, incumbent, nestedFeature, nestedValues[0]);
        var reopenedClosed = Get<IFsClosedValue>(reopened, Id(closed));
        if (edit == "clear-value") Assert.Null(reopenedClosed.ValueRA);
        else Assert.Equal(values[1].ToGuid(), reopenedClosed.ValueRA.Guid);
    }

    [Theory]
    [InlineData("foreign-value", "value must belong to its exact closed feature")]
    [InlineData("duplicate-feature", "feature may occur only once")]
    [InlineData("complex-definition", "requires a closed feature")]
    public void InvalidClosedSelectionBesideNativeComplexRequirementStillRefuses(string scenario, string reason)
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var (otherFeature, otherValues) = AddFeature(_cache, "number", "plural");
        var incumbent = AddNativeComplexRequirement(msa, feature, values[0]);
        var selection = Selection(incumbent.Structure,
            scenario == "complex-definition" ? incumbent.Feature : otherFeature,
            scenario == "foreign-value" ? values[0] : otherValues[0]);
        if (scenario == "duplicate-feature")
            selection = selection.Concat(Selection(incumbent.Structure, otherFeature, otherValues[0])).ToArray();
        var proposal = Proposal(selection);
        var before = BaselineSemanticDigest.Compute(_cache);
        var appliedBefore = ProjectAppliedLog.ReadAll(_cache).ToArray();
        var refusal = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains(reason, refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        Assert.Equal(appliedBefore, ProjectAppliedLog.ReadAll(_cache).ToArray());
        AssertNativeComplexRequirement(_cache, msa, incumbent, feature, values[0]);
        Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC);
    }

    [Fact]
    public void RemovingDeclaredClosedDomainMembershipRefusesWithoutChangingNativeComplexSibling()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "ki", _category);
        var (feature, values) = AddFeature(_cache, "nc", "seven");
        var (closedFeature, closedValues) = AddFeature(_cache, "number", "singular");
        var create = Structure(msa);
        ApplyOps(_cache, [create]);
        ApplyOps(_cache, Selection(create.EntityId!.Value, closedFeature, closedValues[0]));
        var incumbent = AddNativeComplexRequirement(msa, feature, values[0]);
        IFsFeatStrucType type = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            type = _cache.ServiceLocator.GetInstance<IFsFeatStrucTypeFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.TypesOC.Add(type);
            type.FeaturesRS.Add(Get<IFsFeatDefn>(_cache, incumbent.Feature));
            type.FeaturesRS.Add(Get<IFsClosedFeature>(_cache, closedFeature));
            Get<IFsFeatStruc>(_cache, incumbent.Structure).TypeRA = type;
        });
        var remove = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucTypeFeaturesOperationKinds.RemoveRefFeatures,
            target: Id(type), after: JsonSerializer.SerializeToElement(new { member = closedFeature.Value }));
        var proposal = Proposal([remove]);
        var before = BaselineSemanticDigest.Compute(_cache);
        var refusal = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, proposal));
        Assert.Contains("type containing the selected feature", refusal.Message);
        Assert.Equal(before, BaselineSemanticDigest.Compute(_cache));
        Assert.Contains(Get<IFsClosedFeature>(_cache, closedFeature), type.FeaturesRS);
        AssertNativeComplexRequirement(_cache, msa, incumbent, feature, values[0]);
    }

    private OperationEnvelope Text(string kind, CanonicalId target) => new(CanonicalId.Mint(), kind, target: target,
        after: JsonSerializer.SerializeToElement(new { ws = Anal(_cache), text = "changed" }));

    private static OperationEnvelope Reference(string kind, CanonicalId target, CanonicalId reference) =>
        new(CanonicalId.Mint(), kind, target: target, after: JsonSerializer.SerializeToElement(new { @ref = reference.Value }));

    private NativeComplexRequirement AddNativeComplexRequirement(CanonicalId msa, CanonicalId feature, CanonicalId value)
    {
        NativeComplexRequirement result = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            var nativeMsa = Get<IMoInflAffMsa>(_cache, msa);
            nativeMsa.InflFeatsOA ??= _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            var type = _cache.ServiceLocator.GetInstance<IFsFeatStrucTypeFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.TypesOC.Add(type);
            type.FeaturesRS.Add(Get<IFsClosedFeature>(_cache, feature));
            var complexFeature = _cache.ServiceLocator.GetInstance<IFsComplexFeatureFactory>().Create();
            _cache.LangProject.MsFeatureSystemOA.FeaturesOC.Add(complexFeature);
            complexFeature.Name.set_String(_cache.DefaultAnalWs, "agreement");
            complexFeature.Abbreviation.set_String(_cache.DefaultAnalWs, "agr");
            complexFeature.TypeRA = type;
            var complex = _cache.ServiceLocator.GetInstance<IFsComplexValueFactory>().Create();
            nativeMsa.InflFeatsOA.FeatureSpecsOC.Add(complex);
            complex.FeatureRA = complexFeature;
            var nested = _cache.ServiceLocator.GetInstance<IFsFeatStrucFactory>().Create();
            complex.ValueOA = nested;
            nested.TypeRA = type;
            var closed = _cache.ServiceLocator.GetInstance<IFsClosedValueFactory>().Create();
            nested.FeatureSpecsOC.Add(closed);
            closed.FeatureRA = Get<IFsClosedFeature>(_cache, feature);
            closed.ValueRA = Get<IFsSymFeatVal>(_cache, value);
            result = new(Id(nativeMsa.InflFeatsOA), Id(complex), Id(complexFeature), Id(type), Id(nested), Id(closed));
        });
        return result;
    }

    private static void AssertNativeComplexRequirement(LcmCache cache, CanonicalId msa, NativeComplexRequirement expected,
        CanonicalId feature, CanonicalId value)
    {
        var structure = Get<IMoInflAffMsa>(cache, msa).InflFeatsOA!;
        Assert.Equal(expected.Structure.ToGuid(), structure.Guid);
        var complex = Assert.IsAssignableFrom<IFsComplexValue>(
            Assert.Single(structure.FeatureSpecsOC, spec => spec.Guid == expected.Spec.ToGuid()));
        Assert.Equal(structure.Guid, complex.Owner.Guid);
        Assert.Equal(expected.Feature.ToGuid(), complex.FeatureRA.Guid);
        var definition = Assert.IsAssignableFrom<IFsComplexFeature>(complex.FeatureRA);
        Assert.Equal(cache.LangProject.MsFeatureSystemOA.Guid, definition.Owner.Guid);
        Assert.Equal(expected.Type.ToGuid(), definition.TypeRA.Guid);
        var nested = Assert.IsAssignableFrom<IFsFeatStruc>(complex.ValueOA);
        Assert.Equal(expected.NestedStructure.ToGuid(), nested.Guid);
        Assert.Equal(complex.Guid, nested.Owner.Guid);
        Assert.Equal(expected.Type.ToGuid(), nested.TypeRA.Guid);
        Assert.Contains(Get<IFsClosedFeature>(cache, feature), nested.TypeRA.FeaturesRS);
        var closed = Assert.IsAssignableFrom<IFsClosedValue>(Assert.Single(nested.FeatureSpecsOC));
        Assert.Equal(expected.NestedSpec.ToGuid(), closed.Guid);
        Assert.Equal(nested.Guid, closed.Owner.Guid);
        Assert.Equal(feature.ToGuid(), closed.FeatureRA.Guid);
        Assert.Equal(value.ToGuid(), closed.ValueRA.Guid);
    }

    private sealed record NativeComplexRequirement(CanonicalId Structure, CanonicalId Spec, CanonicalId Feature,
        CanonicalId Type, CanonicalId NestedStructure, CanonicalId NestedSpec);
}
