using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Commands;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Runner.Snapshotting;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Composers;

[Collection(LcmCacheParallelCollections.Group0)]
public sealed class AuthorAffixSlotComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;

    public AuthorAffixSlotComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void AuthoringSlotCreatesOwnedIdentityAndAssignsExistingMsaAfterCreation()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var msa = CreateInflectionalMsa(_cache, _seed.FirstEntryId, category);
        var categoryId = Id(category);
        var msaId = Id(msa);
        var slotId = CanonicalId.Mint();
        var createId = CanonicalId.Mint();
        var nameId = CanonicalId.Mint();
        var optionalId = CanonicalId.Mint();
        var assignmentId = CanonicalId.Mint();
        var ids = new Queue<CanonicalId>([slotId, createId, nameId, optionalId, assignmentId]);
        var writingSystem = _cache.WritingSystemFactory.GetStrFromWs(_cache.DefaultAnalWs);

        var operations = AuthorAffixSlotComposer.Build(_cache,
            new AuthorAffixSlotIntent(categoryId, "café", writingSystem, true, [msaId]), () => ids.Dequeue());

        var create = Assert.Single(operations, operation => operation.Kind == PartOfSpeechAffixSlotsOperationKinds.Create);
        Assert.Equal(slotId, create.EntityId);
        Assert.Equal(22, slotId.Value.Length);
        Assert.Equal(slotId, CanonicalId.Parse(slotId.Value));
        var name = Assert.Single(operations, operation => operation.Kind == MoInflAffixSlotNameOperationKinds.SetName);
        Assert.Equal(slotId, name.Target);
        Assert.Equal((writingSystem, "cafe\u0301"), MoInflAffixSlotNameSetPayload.Parse(name.After!.Value));
        var optional = Assert.Single(operations, operation => operation.Kind == MoInflAffixSlotOptionalOperationKinds.SetOptional);
        Assert.Equal(slotId, optional.Target);
        Assert.True(MoInflAffixSlotOptionalSetPayload.Parse(optional.After!.Value));
        var assignment = Assert.Single(operations, operation => operation.Kind == MoInflAffMsaSlotsOperationKinds.AddRefSlots);
        Assert.Equal(msaId, assignment.Target);
        Assert.Equal(slotId, MoInflAffMsaSlotsMemberPayload.Parse(assignment.After!.Value,
            MoInflAffMsaSlotsOperationKinds.AddRefSlots));
        Assert.All(operations.Where(operation => operation != create), operation =>
            Assert.Contains(operation.DependsOn, dependency => dependency.OperationId == create.OperationId));
        Assert.Empty(category.AffixSlotsOC);
        Assert.Empty(msa.SlotsRC);

        var proposal = Proposal(operations.Reverse().ToArray());
        Assert.Empty(ChangeFitPreflight.Check(_cache, proposal));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.Field == SnapshotFields.PartOfSpeechAffixSlots);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == slotId &&
            effect.Field == SnapshotFields.MoInflAffixSlotName);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == slotId &&
            effect.Field == SnapshotFields.MoInflAffixSlotOptional);
        Assert.Contains(dryRun.ExpectedEffects, effect => effect.CanonicalId == msaId &&
            effect.Field == SnapshotFields.MoInflAffMsaSlots);

        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "author-affix-slot-tests");
        var repository = _cache.ServiceLocator.GetInstance<ICmObjectRepository>();
        var created = Assert.IsAssignableFrom<IMoInflAffixSlot>(repository.GetObject(slotId.ToGuid()));
        Assert.Equal(category.Guid, created.Owner?.Guid);
        Assert.Equal("cafe\u0301", created.Name.get_String(_cache.DefaultAnalWs)?.Text);
        Assert.True(created.Optional);
        Assert.Equal([slotId.ToGuid()], msa.SlotsRC.Select(slot => slot.Guid));
        Assert.Contains(receipt.ActualEffects, effect => effect.CanonicalId == slotId &&
            effect.Field == SnapshotFields.MoInflAffixSlotName);

        var after = SlotSnapshot(_cache, created);
        var changes = SoundSystemSnapshots.Compare(
            new Dictionary<CanonicalId, ObjectSnapshot> { [slotId] = ObjectSnapshot.Empty(slotId) },
            new Dictionary<CanonicalId, ObjectSnapshot> { [slotId] = after });
        Assert.Contains(changes, effect => effect.Field == SnapshotFields.MoInflAffixSlotName);
        Assert.Contains(changes, effect => effect.Field == SnapshotFields.MoInflAffixSlotOptional);

        new FwDataProjectLoader().Save(_cache);
        using var reopened = new FwDataProjectLoader().LoadScratchCache(_cache.ProjectId.Path);
        var reopenedRepository = reopened.ServiceLocator.GetInstance<ICmObjectRepository>();
        var reopenedSlot = Assert.IsAssignableFrom<IMoInflAffixSlot>(reopenedRepository.GetObject(slotId.ToGuid()));
        var reopenedMsa = Assert.IsAssignableFrom<IMoInflAffMsa>(reopenedRepository.GetObject(msaId.ToGuid()));
        Assert.Equal(categoryId.ToGuid(), reopenedSlot.Owner?.Guid);
        Assert.Equal("cafe\u0301", reopenedSlot.Name.get_String(reopened.DefaultAnalWs)?.Text);
        Assert.True(reopenedSlot.Optional);
        Assert.Contains(reopenedSlot, reopenedMsa.SlotsRC);
        Assert.Empty(SoundSystemSnapshots.Compare(
            new Dictionary<CanonicalId, ObjectSnapshot> { [slotId] = after },
            new Dictionary<CanonicalId, ObjectSnapshot> { [slotId] = SlotSnapshot(reopened, reopenedSlot) }));
    }

    [Fact]
    public void IntentParserRequiresClosedFieldsAndRejectsDuplicateAssignments()
    {
        var id = CanonicalId.Mint().Value;
        using var unknown = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"TAM","ws":"en","optional":false,"unexpected":true}
            """);
        Assert.Throws<ContractParseException>(() => AuthorAffixSlotIntentParser.Parse(unknown.RootElement));

        using var missingOptional = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"TAM","ws":"en"}
            """);
        Assert.Throws<ContractParseException>(() => AuthorAffixSlotIntentParser.Parse(missingOptional.RootElement));

        using var duplicates = JsonDocument.Parse($$"""
            {"category":"{{id}}","name":"TAM","ws":"en","optional":false,"assignments":["{{id}}","{{id}}"]}
            """);
        Assert.Throws<ContractParseException>(() => AuthorAffixSlotIntentParser.Parse(duplicates.RootElement));
    }

    [Fact]
    public void AuthoringSlotRejectsAnMsaOutsideTheSlotCategoryAndItsDescendants()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var unrelatedCategory = CreateCategory(_cache, "Other category");
        var unrelatedMsa = CreateInflectionalMsa(_cache, _seed.FirstEntryId, unrelatedCategory);

        var exception = Assert.Throws<InvalidOperationException>(() => AuthorAffixSlotComposer.Build(_cache,
            new AuthorAffixSlotIntent(Id(category), "TAM", WritingSystem(_cache), false, [Id(unrelatedMsa)])));

        Assert.Contains("not the affix category or one of its ancestors", exception.Message, StringComparison.Ordinal);
        Assert.Empty(category.AffixSlotsOC);
        Assert.Empty(unrelatedMsa.SlotsRC);
    }

    [Fact]
    public void CreateRefusesSameTypeAndWrongTypeCanonicalIdentityCollisions()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var existingSlot = CreateSlot(_cache, category, "Existing slot");
        var sameType = BuildWithSlotId(_cache, category, Id(existingSlot));

        var reuse = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(sameType)));

        Assert.Contains("overwrite/reuse", reuse.Message, StringComparison.Ordinal);

        var wrongType = BuildWithSlotId(_cache, category, Id(category));
        var conflict = Assert.Throws<InvalidOperationException>(() => ScratchDryRun.Of(_cache, Proposal(wrongType)));

        Assert.Contains("semantic conflict", conflict.Message, StringComparison.Ordinal);
        Assert.Single(category.AffixSlotsOC);
    }

    [Fact]
    public void AssignmentFailureRollsBackTheNewSlotAndItsOtherWrites()
    {
        var category = GetCategory(_cache, _seed.PartOfSpeechId);
        var msa = CreateInflectionalMsa(_cache, _seed.FirstEntryId, category);
        var slotId = CanonicalId.Mint();
        var values = new Queue<CanonicalId>([slotId, CanonicalId.Mint(), CanonicalId.Mint(),
            CanonicalId.Mint(), CanonicalId.Mint()]);
        var proposal = Proposal(AuthorAffixSlotComposer.Build(_cache,
            new AuthorAffixSlotIntent(Id(category), "TAM", WritingSystem(_cache), true, [Id(msa)]),
            () => values.Dequeue()));
        var dryRun = ScratchDryRun.Of(_cache, proposal);

        var exception = Assert.Throws<InvalidOperationException>(() => ProposalApplier.Apply(
            _cache, proposal, dryRun.Anchor, "author-affix-slot-tests", string.Empty,
            afterOperation: (_, operation) =>
            {
                if (operation.Kind == MoInflAffMsaSlotsOperationKinds.AddRefSlots)
                    throw new InvalidOperationException("injected after slot assignment");
            }));

        Assert.Equal("injected after slot assignment", exception.Message);
        Assert.False(_cache.ServiceLocator.GetInstance<ICmObjectRepository>().IsValidObjectId(slotId.ToGuid()));
        Assert.Empty(category.AffixSlotsOC);
        Assert.Empty(msa.SlotsRC);
    }

    private static ObjectSnapshot SlotSnapshot(LcmCache cache, IMoInflAffixSlot slot)
    {
        var fields = new Dictionary<string, IReadOnlyDictionary<string, string>>(
            MoInflAffixSlotAuthoringSnapshotter.Snapshot(cache, slot).AlternativesFields);
        foreach (var (field, value) in MoInflAffixSlotSnapshotter.Snapshot(cache, slot).AlternativesFields)
            fields[field] = value;
        return new ObjectSnapshot(Id(slot), fields);
    }

    private static IPartOfSpeech GetCategory(LcmCache cache, Guid id) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().GetObject(id) as IPartOfSpeech ??
        throw new InvalidOperationException();

    private static IPartOfSpeech CreateCategory(LcmCache cache, string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            category = cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            cache.LangProject.PartsOfSpeechOA.PossibilitiesOS.Add(category);
            category.Name.set_String(cache.DefaultAnalWs, name);
        });
        return category;
    }

    private static IMoInflAffixSlot CreateSlot(LcmCache cache, IPartOfSpeech category, string name)
    {
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            slot = cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(cache.DefaultAnalWs, name);
        });
        return slot;
    }

    private static IReadOnlyList<OperationEnvelope> BuildWithSlotId(LcmCache cache, IPartOfSpeech category,
        CanonicalId slotId)
    {
        var values = new Queue<CanonicalId>([slotId, CanonicalId.Mint(), CanonicalId.Mint(), CanonicalId.Mint()]);
        return AuthorAffixSlotComposer.Build(cache,
            new AuthorAffixSlotIntent(Id(category), "TAM", WritingSystem(cache), false),
            () => values.Dequeue());
    }

    private static string WritingSystem(LcmCache cache) =>
        cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);

    private static IMoInflAffMsa CreateInflectionalMsa(LcmCache cache, Guid entryId, IPartOfSpeech category)
    {
        IMoInflAffMsa msa = null!;
        var entry = cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            msa = cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                entry, SandboxGenericMSA.Create(MsaType.kInfl, category)));
        return msa;
    }

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);

    private static Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);
}
