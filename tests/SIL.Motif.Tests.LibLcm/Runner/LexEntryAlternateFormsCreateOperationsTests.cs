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

namespace SIL.Motif.Tests.Runner;

/// <summary>
/// <c>LexEntry.AlternateForms</c> creation (owning/seq) against a real project: the concrete allomorph
/// class follows the morph type, the authored identity is kept, and the ordered sequence needs placement
/// once it is occupied.
/// </summary>
[Collection(LcmCacheParallelCollections.Group3)]
public sealed class LexEntryAlternateFormsCreateOperationsTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly CanonicalId _category;

    public LexEntryAlternateFormsCreateOperationsTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _category = CanonicalId.FromGuid(pristine.Seed.PartOfSpeechId);
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    private OperationEnvelope Create(CanonicalId entry, Guid morphType, string text = "mw", CanonicalId? formId = null,
        Placement? placement = null, object? after = null) => new(CanonicalId.Mint(),
        LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms, entityId: formId ?? CanonicalId.Mint(),
        target: entry, placement: placement, after: JsonSerializer.SerializeToElement(
            after ?? new { morphType = CanonicalId.FromGuid(morphType).Value, ws = Vern(_cache), text }));

    [Fact]
    public void AffixMorphType_CreatesAnAffixAllomorphWithTheAuthoredIdentityAndSurvivesReload()
    {
        var (entry, _, lexemeForm) = AddInflectionalPrefix(_cache, "m", _category);
        var create = Create(entry, MoMorphTypeTags.kguidMorphPrefix);
        var proposal = Proposal([create]);

        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.LexEntryAlternateForms, effect.Field);
        Assert.Empty(effect.Before);
        Assert.Equal(create.EntityId!.Value.Value, Assert.Single(effect.After).Value);
        ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "alternate-form-tests");

        var form = Assert.IsAssignableFrom<IMoAffixAllomorph>(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));
        Assert.Equal(create.EntityId.Value.ToGuid(), form.Guid);
        Assert.Equal("mw", form.Form.get_String(_cache.DefaultVernWs).Text);
        Assert.Equal(MoMorphTypeTags.kguidMorphPrefix, form.MorphTypeRA!.Guid);
        Assert.Equal(lexemeForm.ToGuid(), Get<ILexEntry>(_cache, entry).LexemeFormOA!.Guid);
        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reloaded = Assert.IsAssignableFrom<IMoAffixAllomorph>(
            Assert.Single(Get<ILexEntry>(reopened, entry).AlternateFormsOS));
        Assert.Equal(form.Guid, reloaded.Guid);
        Assert.Equal("mw", reloaded.Form.get_String(reopened.DefaultVernWs).Text);
        Assert.Equal(create.EntityId.Value.Value, Assert.Single(LexEntryAlternateFormsSnapshotter
            .Snapshot(reopened, Get<ILexEntry>(reopened, entry)).AlternativesFields[SnapshotFields.LexEntryAlternateForms]).Value);
    }

    [Fact]
    public void StemMorphType_CreatesAStemAllomorph()
    {
        var (entry, _, _) = AddStem(_cache, "tu", _category);

        ApplyOps(_cache, [Create(entry, MoMorphTypeTags.kguidMorphStem, "tw")]);

        Assert.IsAssignableFrom<IMoStemAllomorph>(Assert.Single(Get<ILexEntry>(_cache, entry).AlternateFormsOS));
    }

    [Fact]
    public void ANonemptySequence_RequiresPlacementAndInsertsByIdentity()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var first = Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mw");
        ApplyOps(_cache, [first]);
        var firstId = first.EntityId!.Value;

        var missing = Assert.Throws<InvalidOperationException>(() =>
            ScratchDryRun.Of(_cache, Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mu")])));
        Assert.Contains("requires an identity-relative placement", missing.Message, StringComparison.Ordinal);
        var stale = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mu", placement: new Placement(CanonicalId.Mint(), null))])));
        Assert.Contains("placement neighbours", stale.Message, StringComparison.Ordinal);

        var before = Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mu", placement: new Placement(null, firstId));
        var after = Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mi", placement: new Placement(firstId, null));
        ApplyOps(_cache, [before]);
        ApplyOps(_cache, [after]);

        Assert.Equal([before.EntityId!.Value.ToGuid(), firstId.ToGuid(), after.EntityId!.Value.ToGuid()],
            Get<ILexEntry>(_cache, entry).AlternateFormsOS.Select(form => form.Guid));
    }

    [Fact]
    public void AnEmptySequence_RefusesAPlacement()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);

        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry,
            MoMorphTypeTags.kguidMorphPrefix, placement: new Placement(CanonicalId.Mint(), null))])));
    }

    [Fact]
    public void Create_RejectsAnOpenPayloadAMissingPropertyAndAnUnknownMorphType()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value;

        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix,
            after: new { morphType, ws = Vern(_cache), text = "mw", isAbstract = true })])));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix,
            after: new { morphType, ws = Vern(_cache) })])));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix,
            after: new { morphType = CanonicalId.Mint().Value, ws = Vern(_cache), text = "mw" })])));
        Assert.ThrowsAny<Exception>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix,
            after: new { morphType, ws = "zz-notaws", text = "mw" })])));
        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
    }

    [Fact]
    public void Create_OnANonEntry_NamesTheTypeMismatch()
    {
        var (_, msa, _) = AddInflectionalPrefix(_cache, "m", _category);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ScratchDryRun.Of(_cache, Proposal([Create(msa, MoMorphTypeTags.kguidMorphPrefix)])));

        Assert.Contains("not a LexEntry", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RefusesTheZeroIdentityReuseAWrongTypeCollisionAndStorageOverrides()
    {
        var (entry, msa, lexemeForm) = AddInflectionalPrefix(_cache, "m", _category);

        var zero = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry,
            MoMorphTypeTags.kguidMorphPrefix, formId: CanonicalId.FromGuid(Guid.Empty))])));
        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry,
            MoMorphTypeTags.kguidMorphPrefix, formId: lexemeForm)])));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry,
            MoMorphTypeTags.kguidMorphPrefix, formId: msa)])));
        var overridden = new OperationEnvelope(CanonicalId.Mint(),
            LexEntryAlternateFormsCreateOperationKinds.CreateAlternateForms, entityId: CanonicalId.Mint(), target: entry,
            storageIdOverride: CanonicalId.Mint(), after: JsonSerializer.SerializeToElement(new
                { morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphPrefix).Value, ws = Vern(_cache), text = "mw" }));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([overridden])));

        Assert.Contains("zero identity", zero.Message, StringComparison.Ordinal);
        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);
        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
    }

    [Fact]
    public void TwoCreatesOfOneMember_AddressOneSlot()
    {
        var entry = CanonicalId.Mint();
        var member = CanonicalId.Mint();

        Assert.NotNull(ProposalOperationSlotValidator.FindConflict([
            Create(entry, MoMorphTypeTags.kguidMorphPrefix, formId: member),
            Create(entry, MoMorphTypeTags.kguidMorphPrefix, formId: member)]));
        Assert.Null(ProposalOperationSlotValidator.FindConflict([
            Create(entry, MoMorphTypeTags.kguidMorphPrefix), Create(entry, MoMorphTypeTags.kguidMorphPrefix, "mi")]));
    }

    [Fact]
    public void FailureAfterTheCreate_RemovesTheFormAndRestoresTheDigest()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var create = Create(entry, MoMorphTypeTags.kguidMorphPrefix);
        var proposal = Proposal([create]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var digest = BaselineSemanticDigest.Compute(_cache);

        Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(_cache, proposal, dryRun.Anchor,
            "alternate-form-tests", string.Empty,
            afterOperation: (_, _) => throw new InvalidOperationException("injected after the create")));

        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
        Assert.False(Exists(_cache, create.EntityId!.Value));
        Assert.Equal(digest, BaselineSemanticDigest.Compute(_cache));
    }

    [Fact]
    public void AnAppliedAlternateForm_ChangesTheSemanticDigest()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var digest = BaselineSemanticDigest.Compute(_cache);

        ApplyOps(_cache, [Create(entry, MoMorphTypeTags.kguidMorphPrefix)]);

        Assert.NotEqual(digest, BaselineSemanticDigest.Compute(_cache));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RawCreateRefusesAMorphTypeOfTheOtherEntryKind(bool stemEntry)
    {
        var entry = stemEntry ? AddStem(_cache, "tu", _category).Entry
            : AddInflectionalPrefix(_cache, "m", _category).Entry;
        var morphType = stemEntry ? MoMorphTypeTags.kguidMorphPrefix : MoMorphTypeTags.kguidMorphStem;
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([Create(entry, morphType)])));
        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
    }

    [Fact]
    public void RawClassLinkRefusesAForeignCategory()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        var category = AddCategory(_cache, "foreign");
        var cls = AddInflectionClass(_cache, category, "foreign class");
        var create = Create(entry, MoMorphTypeTags.kguidMorphPrefix);
        var link = new OperationEnvelope(CanonicalId.Mint(), MoAffixFormInflectionClassesOperationKinds.AddRefInflectionClasses,
            target: create.EntityId, after: JsonSerializer.SerializeToElement(new { member = cls.Value }),
            dependsOn: [new OperationDependency(create.OperationId)]);
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal([create, link])));
        Assert.Empty(Get<ILexEntry>(_cache, entry).AlternateFormsOS);
    }

    [Fact]
    public void RawAlternateCreateRefusesAnEntryWithAProcessAlternate()
    {
        var (entry, _, _) = AddInflectionalPrefix(_cache, "m", _category);
        SIL.LCModel.Infrastructure.NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            Get<ILexEntry>(_cache, entry).AlternateFormsOS.Add(
                _cache.ServiceLocator.GetInstance<IMoAffixProcessFactory>().Create()));
        Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache,
            Proposal([Create(entry, MoMorphTypeTags.kguidMorphPrefix,
                placement: new Placement(Id(Get<ILexEntry>(_cache, entry).AlternateFormsOS[0]), null))])));
    }

}
