using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.LiveHost.Baselines;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.Composers;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using static SIL.Motif.Tests.Composers.AffixTestSupport;
using static SIL.Motif.Tests.Composers.InflFeatsTestSupport;

namespace SIL.Motif.Tests.Runner;

/// <summary>
/// <c>MoDerivAffMsa.FromMsFeatures</c> and <c>ToMsFeatures</c> (owning/atomic) against a real project: each is created
/// empty into an empty slot and never replaces an occupant.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class MoDerivAffMsaMsFeaturesOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public MoDerivAffMsaMsFeaturesOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    public static TheoryData<string> Kinds() => new()
    {
        MoDerivAffMsaMsFeaturesOperationKinds.CreateFromMsFeatures,
        MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures,
    };

    private static string FieldOf(string kind) => kind == MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures
        ? SnapshotFields.MoDerivAffMsaToMsFeatures : SnapshotFields.MoDerivAffMsaFromMsFeatures;

    private static IFsFeatStruc? Slot(IMoDerivAffMsa msa, string kind) =>
        kind == MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures ? msa.ToMsFeaturesOA : msa.FromMsFeaturesOA;

    private CanonicalId Derivational() => AddDerivational();

    private CanonicalId AddDerivational() => Created(ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache,
        new AuthorLexicalEntryIntent("pa", Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "pa", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, _category)))),
        LexEntryMorphoSyntaxAnalysesOperationKinds.Create);

    private static OperationEnvelope Create(string kind, CanonicalId msa, CanonicalId? featStruc = null, object? after = null) => new(
        CanonicalId.Mint(), kind, entityId: featStruc ?? CanonicalId.Mint(), target: msa,
        after: JsonSerializer.SerializeToElement(after ?? new { }));

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_IntoAnEmptySlot_KeepsTheAuthoredIdentityThroughDryRunApplyAndReload(string kind)
    {
        var msa = Derivational();
        var digestBefore = BaselineSemanticDigest.Compute(_cache);
        var create = Create(kind, msa);
        var proposal = Proposal([create]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var effect = Assert.Single(dryRun.ExpectedEffects, item => item.Field != "dryRunWarning");
        Assert.Equal(FieldOf(kind), effect.Field);
        Assert.Empty(effect.Before);
        Assert.Equal(create.EntityId!.Value.Value, Assert.Single(effect.After).Value);
        Assert.Null(Slot(Get<IMoDerivAffMsa>(_cache, msa), kind));

        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "deriv-features-tests");

        Assert.Equal(create.EntityId.Value.ToGuid(), Slot(Get<IMoDerivAffMsa>(_cache, msa), kind)!.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloaded = Get<IMoDerivAffMsa>(reopened, msa);
        Assert.Equal(create.EntityId.Value.ToGuid(), Slot(reloaded, kind)!.Guid);
        var snapshot = MoDerivAffMsaMsFeaturesSnapshotter.Snapshot(reopened, reloaded);
        Assert.Equal(create.EntityId.Value.Value, Assert.Single(snapshot.AlternativesFields[FieldOf(kind)]).Value);
        Assert.NotEqual(digestBefore, BaselineSemanticDigest.Compute(reopened));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_IntoAnOccupiedSlot_IsRefusedAndTheIncumbentSurvives(string kind)
    {
        var msa = Derivational();
        var first = Create(kind, msa);
        ApplyOps(_cache, [first]);

        var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(kind, msa)])));

        Assert.Contains("occupied", exception.Message, StringComparison.Ordinal);
        Assert.Equal(first.EntityId!.Value.ToGuid(), Slot(Get<IMoDerivAffMsa>(_cache, msa), kind)!.Guid);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FromAndToAreIndependentSlots(string kind)
    {
        var msa = Derivational();
        ApplyOps(_cache, [Create(kind, msa)]);
        var other = kind == MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures
            ? MoDerivAffMsaMsFeaturesOperationKinds.CreateFromMsFeatures : MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures;
        Assert.Null(Slot(Get<IMoDerivAffMsa>(_cache, msa), other));
        ApplyOps(_cache, [Create(other, msa)]);
        Assert.NotNull(Slot(Get<IMoDerivAffMsa>(_cache, msa), other));
        Assert.NotNull(Slot(Get<IMoDerivAffMsa>(_cache, msa), kind));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_RejectsAnyPayloadProperty(string kind)
    {
        var msa = Derivational();
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(kind, msa, after: new { type = "x" })])));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_OnAStemOrInflectionalMsa_NamesTheTypeMismatch(string kind)
    {
        var (_, stemMsa, _) = AddStem(_cache, "tu", _category);
        var (_, inflMsa, _) = AddInflectionalPrefix(_cache, "m", _category);

        foreach (var wrong in new[] { stemMsa, inflMsa })
        {
            var exception = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(kind, wrong)])));
            Assert.Contains("not a MoDerivAffMsa", exception.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_RefusesTheZeroIdentityReuseAndAWrongTypeCollision(string kind)
    {
        var msa = Derivational();
        var other = Created(ApplyOps(_cache, AuthorLexicalEntryComposer.Build(_cache, new AuthorLexicalEntryIntent("qa",
            Vern(_cache), MorphType(MoMorphTypeTags.kguidMorphPrefix), "qa", Anal(_cache),
            new LexicalEntryMsa(LexicalEntryMsaKind.Derivational, _category, _category)))),
            LexEntryMorphoSyntaxAnalysesOperationKinds.Create);
        var existing = Create(kind, other);
        ApplyOps(_cache, [existing]);

        var zero = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(kind, msa, CanonicalId.FromGuid(Guid.Empty))])));
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(kind, msa, existing.EntityId)])));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(kind, msa, other)])));

        Assert.Contains("zero identity", zero.Message, StringComparison.Ordinal);
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Null(Slot(Get<IMoDerivAffMsa>(_cache, msa), kind));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_RefusesStorageOverridesAndPlacement(string kind)
    {
        var msa = Derivational();
        var overridden = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: CanonicalId.Mint(), target: msa,
            after: JsonSerializer.SerializeToElement(new { }), storageIdOverride: CanonicalId.Mint());
        var placed = new OperationEnvelope(CanonicalId.Mint(), kind, entityId: CanonicalId.Mint(), target: msa,
            after: JsonSerializer.SerializeToElement(new { }), placement: new Placement(CanonicalId.Mint(), null));

        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([overridden])));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([placed])));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_TwiceInOneProposal_AddressesOneSlot(string kind)
    {
        var msa = CanonicalId.Mint();

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([Create(kind, msa), Create(kind, msa)]));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Create_ConflictsWhenTheSlotIsFilledBetweenDryRunAndApply(string kind)
    {
        var msa = Derivational();
        var proposal = Proposal([Create(kind, msa)]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        ApplyOps(_cache, [Create(kind, msa)]);

        Assert.ThrowsAny<Exception>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "deriv-features-tests"));
        Assert.Equal(1, _cache.ServiceLocator.GetInstance<IFsFeatStrucRepository>().AllInstances()
            .Count(structure => structure.Owner?.Guid == msa.ToGuid()));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void FailureAfterTheCreate_LeavesTheProjectAsItWas(string kind)
    {
        var msa = Derivational();
        var create = Create(kind, msa);
        var proposal = Proposal([create]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "deriv-features-tests", string.Empty,
            afterOperation: (_, _) => throw new InvalidOperationException("injected after the create")));

        Assert.Null(Slot(Get<IMoDerivAffMsa>(_cache, msa), kind));
        Assert.False(Exists(_cache, create.EntityId!.Value));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }

    [Theory]
    [InlineData("missing-target")]
    [InlineData("missing-entity")]
    [InlineData("missing-after")]
    [InlineData("scalar-after")]
    [InlineData("missing-object")]
    public void CreateRefusesIncompleteOrMalformedEnvelopes(string scenario)
    {
        var msa = Derivational();
        var operation = new OperationEnvelope(CanonicalId.Mint(), MoDerivAffMsaMsFeaturesOperationKinds.CreateToMsFeatures,
            target: scenario == "missing-target" ? null : scenario == "missing-object" ? CanonicalId.Mint() : msa,
            entityId: scenario == "missing-entity" ? null : CanonicalId.Mint(),
            after: scenario == "missing-after" ? null : JsonSerializer.SerializeToElement(scenario == "scalar-after" ? (object)42 : new { }));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([operation])));
        Assert.Null(Get<IMoDerivAffMsa>(_cache, msa).ToMsFeaturesOA);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ASelectionOfAFeatureOutsideTheMorphosyntacticSystemIsRefused(string kind)
    {
        var msa = Derivational();
        var (feature, values) = AddFeature(_cache, "case", "nominative");
        var structure = Create(kind, msa);
        ApplyOps(_cache, [structure]);
        MoveToPhonologicalSystem(feature);

        var selection = Selection(structure.EntityId!.Value, feature, values[0]);
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal(selection)));
    }

    private void MoveToPhonologicalSystem(CanonicalId feature)
    {
        // Move the feature to the phonological system so it is outside the morphosyntactic one.
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            _cache.LangProject.PhFeatureSystemOA ??= _cache.ServiceLocator.GetInstance<IFsFeatureSystemFactory>().Create();
            _cache.LangProject.PhFeatureSystemOA.FeaturesOC.Add(Get<IFsClosedFeature>(_cache, feature));
        });
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void TwoSpecificationsOfOneFeatureAreRefused(string kind)
    {
        var msa = Derivational();
        var (feature, values) = AddFeature(_cache, "case", "nominative");
        var structure = Create(kind, msa);
        ApplyOps(_cache, [structure]);
        ApplyOps(_cache, Selection(structure.EntityId!.Value, feature, values[0]));

        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache,
            Proposal(Selection(structure.EntityId!.Value, feature, values[0]))));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ADerivationalStructureAddsItsOwningLinkAndSpecificationsToMechanicalDiff(string kind)
    {
        var msa = Derivational();
        var (feature, values) = AddFeature(_cache, "case", "nominative");
        var before = InflectionSnapshots.Read(_cache);
        var structure = Create(kind, msa);
        ApplyOps(_cache, [structure]);
        var created = InflectionSnapshots.Read(_cache);
        var link = Assert.Single(SoundSystemSnapshots.Compare(before, created), effect => effect.Field == FieldOf(kind));
        Assert.Equal(msa, link.CanonicalId);
        Assert.Empty(link.Before);
        Assert.Equal(structure.EntityId!.Value.Value, link.After["ref"]);

        var selection = Selection(structure.EntityId!.Value, feature, values[0]);
        ApplyOps(_cache, selection);
        var effects = SoundSystemSnapshots.Compare(created, InflectionSnapshots.Read(_cache));
        Assert.Contains(effects, effect => effect.Field == SnapshotFields.FsFeatStrucFeatureSpecs &&
            effect.After.Values.Contains(selection[0].EntityId!.Value.Value));
        Assert.Contains(effects, effect => effect.CanonicalId == selection[0].EntityId &&
            effect.Field == SnapshotFields.FsClosedValueValue && effect.After["ref"] == values[0].Value);
    }
}
