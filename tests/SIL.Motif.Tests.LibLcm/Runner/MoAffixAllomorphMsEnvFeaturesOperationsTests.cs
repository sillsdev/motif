using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.InflFeatsTestSupport;

namespace SIL.Motif.Tests.Runner;

/// <summary>
/// <c>MoAffixAllomorph.MsEnvFeatures</c> (owning/atomic) against a real project: an allomorph's required
/// feature structure is created empty into an empty slot, and the slot is never replaced.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MoAffixAllomorphMsEnvFeaturesOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public MoAffixAllomorphMsEnvFeaturesOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private static OperationEnvelope Create(CanonicalId form, CanonicalId? featStruc = null, object? after = null) => new(
        CanonicalId.Mint(), MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures, entityId: featStruc ?? CanonicalId.Mint(),
        target: form, after: JsonSerializer.SerializeToElement(after ?? new { }));

    [Fact]
    public void Create_IntoAnEmptySlot_KeepsTheAuthoredIdentityThroughDryRunApplyAndReload()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var digestBefore = BaselineSemanticDigest.Compute(_cache);
        var create = Create(form);
        var proposal = Proposal([create]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoAffixAllomorphMsEnvFeatures, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Equal(create.EntityId!.Value.Value, Assert.Single(effect.After).Value);
        Assert.Null(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "inflection-features-tests");

        Assert.Equal(create.EntityId.Value.ToGuid(), Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA!.Guid);
        Assert.Empty(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA!.FeatureSpecsOC);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloaded = Get<IMoAffixAllomorph>(reopened, form);
        Assert.Equal(create.EntityId.Value.ToGuid(), reloaded.MsEnvFeaturesOA!.Guid);
        var snapshot = MoAffixAllomorphMsEnvFeaturesSnapshotter.Snapshot(reopened, reloaded);
        Assert.Equal(create.EntityId.Value.Value, Assert.Single(snapshot.AlternativesFields[SnapshotFields.MoAffixAllomorphMsEnvFeatures]).Value);
        Assert.NotEqual(digestBefore, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void Create_IntoAnOccupiedSlot_IsRefusedAndTheIncumbentSurvives()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var first = Create(form);
        ApplyOps(_cache, [first]);

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(form)])));

        Assert.Contains("occupied", exception.Message, StringComparison.Ordinal);
        Assert.Equal(first.EntityId!.Value.ToGuid(), Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA!.Guid);
    }

    [Fact]
    public void Create_RejectsAnyPayloadProperty()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(form, after: new { type = "x" })])));
    }

    [Fact]
    public void Create_OnAStemForm_NamesTheTypeMismatch()
    {
        var (_, _, stemMsa) = AddStem(_cache, "tu", _category);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ScratchDryRun.Of(_cache, Proposal([Create(stemMsa)])));

        Assert.Contains("not a MoAffixAllomorph", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesTheZeroIdentityReuseAndAWrongTypeCollision()
    {
        var (entry, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var (_, _, otherMsa) = AddInflectionalPrefix(_cache, "n", _category);
        var existing = Create(otherMsa);
        ApplyOps(_cache, [existing]);

        var zero = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(form, CanonicalId.FromGuid(Guid.Empty))])));
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(form, existing.EntityId)])));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(form, entry)])));

        Assert.Contains("zero identity", zero.Message, StringComparison.Ordinal);
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Null(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA);
    }

    [Fact]
    public void Create_RefusesStorageOverrides()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var create = new OperationEnvelope(CanonicalId.Mint(), MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures,
            entityId: CanonicalId.Mint(), target: form, after: JsonSerializer.SerializeToElement(new { }),
            storageIdOverride: CanonicalId.Mint());

        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([create])));
    }

    [Fact]
    public void Create_TwiceInOneProposal_AddressesOneSlot()
    {
        var form = CanonicalId.Mint();

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([Create(form), Create(form)]));
    }

    [Fact]
    public void FailureAfterTheCreate_LeavesTheProjectAsItWas()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var create = Create(form);
        var proposal = Proposal([create]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "inflection-features-tests", string.Empty,
            afterOperation: (_, _) => throw new InvalidOperationException("injected after the create")));

        Assert.Null(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA);
        Assert.False(Exists(_cache, create.EntityId!.Value));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }
    [Fact]
    public void AtomicFeatureCreateRefusesPlacement()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures,
            entityId: CanonicalId.Mint(), target: form, after: JsonSerializer.SerializeToElement(new { }),
            placement: new Placement(CanonicalId.Mint(), null));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
    }

    [Fact]
    public void ClosedSpecificationCreateKeepsItsExactAuthoredIdentity()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var structure = Create(form);
        ApplyOps(_cache, [structure]);
        var specId = CanonicalId.Mint("spec:");
        var createSpec = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            entityId: specId, target: structure.EntityId, after: JsonSerializer.SerializeToElement(new { }));
        var dryRun = ScratchDryRun.Of(_cache, Proposal([createSpec]));
        Assert.Equal(CanonicalId.FromGuid(specId.ToGuid()).Value, Assert.Single(dryRun.ExpectedEffects).After["ref"]);
        ApplyOps(_cache, [createSpec]);
        Assert.Equal(specId.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA!.FeatureSpecsOC).Guid);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("same")]
    [InlineData("wrong")]
    [InlineData("override")]
    [InlineData("placement")]
    public void ClosedSpecificationCreateRefusesUnsafeIdentityOrShape(string scenario)
    {
        var (entry, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var structure = Create(form);
        ApplyOps(_cache, [structure]);
        var specId = CanonicalId.Mint();
        var createSpec = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            entityId: specId, target: structure.EntityId, after: JsonSerializer.SerializeToElement(new { }));
        ApplyOps(_cache, [createSpec]);
        var entity = scenario switch { "zero" => CanonicalId.FromGuid(Guid.Empty), "same" => specId,
            "wrong" => entry, _ => CanonicalId.Mint() };
        var invalid = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            entityId: entity, target: structure.EntityId, after: JsonSerializer.SerializeToElement(new { }),
            storageIdOverride: scenario == "override" ? CanonicalId.Mint() : null,
            placement: scenario == "placement" ? new Placement(specId, null) : null);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([invalid])));
        Assert.Equal(specId.ToGuid(), Assert.Single(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA!.FeatureSpecsOC).Guid);
    }

    [Fact]
    public void RawRequiredSelectionRefusesAValueOfAnotherFeature()
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var (feature, _) = AddFeature(_cache, "number", "one");
        var (_, values) = AddFeature(_cache, "case", "nominative");
        var structure = Create(form);
        var specId = CanonicalId.Mint();
        var spec = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            target: structure.EntityId, entityId: specId, after: JsonSerializer.SerializeToElement(new { }),
            dependsOn: [new OperationDependency(structure.OperationId)]);
        var setFeature = new OperationEnvelope(CanonicalId.Mint(), FsFeatureSpecificationFeatureOperationKinds.SetFeature,
            target: specId, after: JsonSerializer.SerializeToElement(new { @ref = feature.Value }),
            dependsOn: [new OperationDependency(spec.OperationId)]);
        var setValue = new OperationEnvelope(CanonicalId.Mint(), FsClosedValueValueOperationKinds.SetValue,
            target: specId, after: JsonSerializer.SerializeToElement(new { @ref = values[0].Value }),
            dependsOn: [new OperationDependency(spec.OperationId)]);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([structure, spec, setFeature, setValue])));
        Assert.Null(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA);
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-entity")]
    [InlineData("missing-after")]
    [InlineData("scalar-after")]
    [InlineData("missing-object")]
    public void CreateRefusesIncompleteOrMalformedEnvelopes(string scenario)
    {
        var (_, _, form) = AddInflectionalPrefix(_cache, "m", _category);
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoAffixAllomorphMsEnvFeaturesOperationKinds.CreateMsEnvFeatures,
            target: scenario == "missing-target" ? null : scenario == "missing-object" ? CanonicalId.Mint() : form,
            entityId: scenario == "missing-entity" ? null : CanonicalId.Mint(),
            after: scenario == "missing-after" ? null : JsonSerializer.SerializeToElement(scenario == "scalar-after" ? (object)42 : new { }));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
        Assert.Null(Get<IMoAffixAllomorph>(_cache, form).MsEnvFeaturesOA);
    }

}
