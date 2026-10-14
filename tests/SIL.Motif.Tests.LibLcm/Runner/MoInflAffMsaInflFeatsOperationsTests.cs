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
/// <c>MoInflAffMsa.InflFeats</c> (owning/atomic) against a real project: an inflectional affix's required
/// feature structure is created empty into an empty slot, and the slot is never replaced.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MoInflAffMsaInflFeatsOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public MoInflAffMsaInflFeatsOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private static OperationEnvelope Create(CanonicalId msa, CanonicalId? featStruc = null, object? after = null) => new(
        CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats, entityId: featStruc ?? CanonicalId.Mint(),
        target: msa, after: JsonSerializer.SerializeToElement(after ?? new { }));

    [Fact]
    public void Create_IntoAnEmptySlot_KeepsTheAuthoredIdentityThroughDryRunApplyAndReload()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var digestBefore = BaselineSemanticDigest.Compute(_cache);
        var create = Create(msa);
        var proposal = Proposal([create]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoInflAffMsaInflFeats, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Equal(create.EntityId!.Value.Value, Assert.Single(effect.After).Value);
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "inflection-features-tests");

        Assert.Equal(create.EntityId.Value.ToGuid(), Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid);
        Assert.Empty(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloaded = Get<IMoInflAffMsa>(reopened, msa);
        Assert.Equal(create.EntityId.Value.ToGuid(), reloaded.InflFeatsOA!.Guid);
        var snapshot = MoInflAffMsaInflFeatsSnapshotter.Snapshot(reopened, reloaded);
        Assert.Equal(create.EntityId.Value.Value, Assert.Single(snapshot.AlternativesFields[SnapshotFields.MoInflAffMsaInflFeats]).Value);
        Assert.NotEqual(digestBefore, BaselineSemanticDigest.Compute(reopened));
    }

    [Fact]
    public void Create_IntoAnOccupiedSlot_IsRefusedAndTheIncumbentSurvives()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var first = Create(msa);
        ApplyOps(_cache, [first]);

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(msa)])));

        Assert.Contains("occupied", exception.Message, StringComparison.Ordinal);
        Assert.Equal(first.EntityId!.Value.ToGuid(), Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.Guid);
    }

    [Fact]
    public void Create_RejectsAnyPayloadProperty()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(msa, after: new { type = "x" })])));
    }

    [Fact]
    public void Create_OnAStemMsa_NamesTheTypeMismatch()
    {
        var (_, stemMsa, _) = AddStem(_cache, "tu", _category);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ScratchDryRun.Of(_cache, Proposal([Create(stemMsa)])));

        Assert.Contains("not a MoInflAffMsa", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesTheZeroIdentityReuseAndAWrongTypeCollision()
    {
        var (entry, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var (_, otherMsa, _) = AddInflectionalPrefix(_cache, "n", _category);
        var existing = Create(otherMsa);
        ApplyOps(_cache, [existing]);

        var zero = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(msa, CanonicalId.FromGuid(Guid.Empty))])));
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(msa, existing.EntityId)])));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(msa, entry)])));

        Assert.Contains("zero identity", zero.Message, StringComparison.Ordinal);
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
    }

    [Fact]
    public void Create_RefusesStorageOverrides()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var create = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
            entityId: CanonicalId.Mint(), target: msa, after: JsonSerializer.SerializeToElement(new { }),
            storageIdOverride: CanonicalId.Mint());

        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([create])));
    }

    [Fact]
    public void Create_TwiceInOneProposal_AddressesOneSlot()
    {
        var msa = CanonicalId.Mint();

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([Create(msa), Create(msa)]));
    }

    [Fact]
    public void FailureAfterTheCreate_LeavesTheProjectAsItWas()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var create = Create(msa);
        var proposal = Proposal([create]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "inflection-features-tests", string.Empty,
            afterOperation: (_, _) => throw new InvalidOperationException("injected after the create")));

        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
        Assert.False(Exists(_cache, create.EntityId!.Value));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }
    [Fact]
    public void AtomicFeatureCreateRefusesPlacement()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
            entityId: CanonicalId.Mint(), target: msa, after: JsonSerializer.SerializeToElement(new { }),
            placement: new Placement(CanonicalId.Mint(), null));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
    }

    [Fact]
    public void ClosedSpecificationCreateKeepsItsExactAuthoredIdentity()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var structure = Create(msa);
        ApplyOps(_cache, [structure]);
        var specId = CanonicalId.Mint("spec:");
        var createSpec = new OperationEnvelope(CanonicalId.Mint(), FsFeatStrucFeatureSpecsOperationKinds.CreateFeatureSpecs,
            entityId: specId, target: structure.EntityId, after: JsonSerializer.SerializeToElement(new { }));
        var dryRun = ScratchDryRun.Of(_cache, Proposal([createSpec]));
        Assert.Equal(CanonicalId.FromGuid(specId.ToGuid()).Value, Assert.Single(dryRun.ExpectedEffects).After["ref"]);
        ApplyOps(_cache, [createSpec]);
        Assert.Equal(specId.ToGuid(), Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC).Guid);
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("same")]
    [InlineData("wrong")]
    [InlineData("override")]
    [InlineData("placement")]
    public void ClosedSpecificationCreateRefusesUnsafeIdentityOrShape(string scenario)
    {
        var (entry, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var structure = Create(msa);
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
        Assert.Equal(specId.ToGuid(), Assert.Single(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA!.FeatureSpecsOC).Guid);
    }

    [Fact]
    public void RawRequiredSelectionRefusesAValueOfAnotherFeature()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var (feature, _) = AddFeature(_cache, "number", "one");
        var (_, values) = AddFeature(_cache, "case", "nominative");
        var structure = Create(msa);
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
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-entity")]
    [InlineData("missing-after")]
    [InlineData("scalar-after")]
    [InlineData("missing-object")]
    public void CreateRefusesIncompleteOrMalformedEnvelopes(string scenario)
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoInflAffMsaInflFeatsOperationKinds.CreateInflFeats,
            target: scenario == "missing-target" ? null : scenario == "missing-object" ? CanonicalId.Mint() : msa,
            entityId: scenario == "missing-entity" ? null : CanonicalId.Mint(),
            after: scenario == "missing-after" ? null : JsonSerializer.SerializeToElement(scenario == "scalar-after" ? (object)42 : new { }));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
        Assert.Null(Get<IMoInflAffMsa>(_cache, msa).InflFeatsOA);
    }

}
