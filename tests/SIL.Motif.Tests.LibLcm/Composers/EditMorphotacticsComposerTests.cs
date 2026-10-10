using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Snapshot;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using Xunit;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Commands;

namespace SIL.Motif.Tests.Composers;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group0)]
public sealed class EditMorphotacticsComposerTests : IDisposable
{
    private readonly LcmCache _cache;
    private readonly SeededProject _seed;
    private readonly IPartOfSpeech _category;

    public EditMorphotacticsComposerTests(PristineProjectFixture pristine)
    {
        _cache = pristine.NewScratch();
        _seed = pristine.Seed;
        _category = _cache.ServiceLocator.GetInstance<ICmObjectRepository>()
            .GetObject(_seed.PartOfSpeechId) as IPartOfSpeech ?? throw new InvalidOperationException();
    }

    public void Dispose()
    {
        if (!_cache.IsDisposed) _cache.Dispose();
    }

    [Fact]
    public void EditAffixSlotChangesOnlyOptionality()
    {
        var slot = CreateSlot(_category, "TAM", optional: false);
        var intent = new EditAffixSlotIntent(Id(slot), ExpectedOptional: false, Optional: true);

        var operation = Assert.Single(EditAffixSlotComposer.Build(_cache, intent));
        Assert.Equal(MoInflAffixSlotOptionalOperationKinds.SetOptional, operation.Kind);
        Assert.Equal(Id(slot), operation.Target);
        Assert.True(MoInflAffixSlotOptionalSetPayload.Parse(operation.After!.Value));
        Assert.False(slot.Optional);

        var unrelatedName = slot.Name.BestAnalysisAlternative.Text;
        var proposal = Proposal(EditAffixSlotComposer.Build(_cache, intent));
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.True(slot.Optional);
        Assert.Equal(unrelatedName, slot.Name.BestAnalysisAlternative.Text);
        Assert.Equal("true", Assert.Single(receipt.ActualEffects).After[BooleanFieldAlternatives.Key]);
    }

    [Fact]
    public void EditAffixTemplateUsesExplicitPrefixPlacementAndLeavesOtherFieldsAlone()
    {
        var first = CreateSlot(_category, "P1");
        var second = CreateSlot(_category, "P2");
        var suffix = CreateSlot(_category, "S1");
        var template = CreateTemplate(_category, [first, second], [suffix]);
        var originalName = template.Name.BestAnalysisAlternative.Text;
        var intent = new EditAffixTemplateIntent(Id(template),
            [Id(first), Id(second)], [Id(second), Id(first)],
            [Id(suffix)], [Id(suffix)]);

        var operation = Assert.Single(EditAffixTemplateComposer.Build(_cache, intent));

        Assert.Equal(MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots, operation.Kind);
        Assert.Equal(Id(template), operation.Target);
        Assert.Equal(Id(first), MoInflAffixTemplatePrefixSlotsMemberPayload.Parse(operation.After!.Value,
            MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots));
        Assert.Equal(new Placement(Id(second), null), operation.Placement);
        Assert.Empty(operation.DependsOn);

        var proposal = Proposal([operation]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var effect = Assert.Single(dryRun.ExpectedEffects);
        Assert.Equal(SnapshotFields.MoInflAffixTemplatePrefixSlots, effect.Field);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Equal([second.Guid, first.Guid], template.PrefixSlotsRS.Select(item => item.Guid));
        Assert.Equal([suffix.Guid], template.SuffixSlotsRS.Select(item => item.Guid));
        Assert.Equal(originalName, template.Name.BestAnalysisAlternative.Text);
        _ = Assert.Single(receipt.ActualEffects);
    }

    [Fact]
    public void EditInflectionalAffixAllowsSlotsFromItsCategoryOrAnAncestorAndChangesNoOtherField()
    {
        var parentSlot = CreateSlot(_category, "Parent");
        var child = CreateCategory(_category, "Child");
        var msa = CreateInflectionalMsa(_seed.FirstEntryId, child);
        var originalCategory = msa.PartOfSpeechRA;
        var intent = new EditInflectionalAffixIntent(Id(msa), [], [Id(parentSlot)]);

        var operation = Assert.Single(EditInflectionalAffixComposer.Build(_cache, intent));

        Assert.Equal(MoInflAffMsaSlotsOperationKinds.AddRefSlots, operation.Kind);
        Assert.Equal(Id(msa), operation.Target);
        Assert.Equal(Id(parentSlot).Value,
            MoInflAffMsaSlotsMemberPayload.Parse(operation.After!.Value,
                MoInflAffMsaSlotsOperationKinds.AddRefSlots).Value);
        Assert.Empty(msa.SlotsRC);

        var proposal = Proposal([operation]);
        var dryRun = ScratchDryRun.Of(_cache, proposal);
        var receipt = ProposalApplier.Apply(_cache, proposal, dryRun.Anchor, "motif-tests");

        Assert.Equal([parentSlot.Guid], msa.SlotsRC.Select(item => item.Guid));
        Assert.Same(originalCategory, msa.PartOfSpeechRA);
        Assert.Equal(Id(parentSlot).Value, Assert.Single(receipt.ActualEffects).After.Values.Single());
    }

    [Fact]
    public void EditInflectionalAffixRejectsWrongMsaSubtypeAndUnrelatedCategorySlot()
    {
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(_seed.FirstEntryId);
        var stem = Assert.IsAssignableFrom<IMoStemMsa>(entry.MorphoSyntaxAnalysesOC.Single());
        var stemError = Assert.Throws<InvalidOperationException>(() => EditInflectionalAffixComposer.Build(_cache,
            new EditInflectionalAffixIntent(Id(stem), [], [])));
        Assert.Contains("existing inflectional affix", stemError.Message, StringComparison.OrdinalIgnoreCase);

        var unrelatedCategory = CreateCategory(null, "Other");
        var unrelatedSlot = CreateSlot(unrelatedCategory, "Other slot");
        var msa = CreateInflectionalMsa(_seed.FirstEntryId, _category);
        var slotError = Assert.Throws<InvalidOperationException>(() => EditInflectionalAffixComposer.Build(_cache,
            new EditInflectionalAffixIntent(Id(msa), [], [Id(unrelatedSlot)])));
        Assert.Contains("does not belong to the affix category or one of its ancestors", slotError.Message,
            StringComparison.Ordinal);
        Assert.Empty(msa.SlotsRC);
    }

    [Fact]
    public void PreflightLeavesAuthoredTemplateOrderAndIntentDigestUnchanged()
    {
        var first = CreateSlot(_category, "P1");
        var second = CreateSlot(_category, "P2");
        var template = CreateTemplate(_category, [first, second], []);
        var operation = Assert.Single(EditAffixTemplateComposer.Build(_cache,
            new EditAffixTemplateIntent(Id(template), [Id(first), Id(second)], [Id(second), Id(first)], [], [])));
        var proposal = Proposal([operation]);
        var intentDigest = IntentDigest.Compute(proposal);

        var fitResults = ChangeFitPreflight.Check(_cache, proposal);

        Assert.Empty(fitResults);
        Assert.Equal(intentDigest, IntentDigest.Compute(proposal));
        Assert.Equal(operation.Placement, Assert.Single(proposal.Operations).Placement);
        Assert.Equal(Id(first), MoInflAffixTemplatePrefixSlotsMemberPayload.Parse(
            proposal.Operations[0].After!.Value, MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots));
    }

    [Fact]
    public void ParsersRejectUnknownPropertiesAndDuplicateSlotIds()
    {
        var id = CanonicalId.FromGuid(Guid.Empty).Value;
        using var slotIntent = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = id,
            expectedOptional = false,
            optional = true,
            name = "TAM",
        }));
        Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(
            () => EditAffixSlotIntentParser.Parse(slotIntent.RootElement));

        using var msaIntent = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            target = id,
            expectedSlots = Array.Empty<string>(),
            slots = new[] { id, id },
        }));
        Assert.Throws<SIL.Motif.Contract.Parsing.ContractParseException>(
            () => EditInflectionalAffixIntentParser.Parse(msaIntent.RootElement));
    }

    private IMoInflAffixSlot CreateSlot(IPartOfSpeech category, string name, bool optional = false)
    {
        IMoInflAffixSlot slot = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            slot = _cache.ServiceLocator.GetInstance<IMoInflAffixSlotFactory>().Create();
            category.AffixSlotsOC.Add(slot);
            slot.Name.set_String(_cache.DefaultAnalWs, name);
            slot.Optional = optional;
        });
        return slot;
    }

    private IPartOfSpeech CreateCategory(IPartOfSpeech? parent, string name)
    {
        IPartOfSpeech category = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            category = _cache.ServiceLocator.GetInstance<IPartOfSpeechFactory>().Create();
            (parent is null ? _cache.LangProject.PartsOfSpeechOA.PossibilitiesOS : parent.SubPossibilitiesOS)
                .Add(category);
            category.Name.set_String(_cache.DefaultAnalWs, name);
        });
        return category;
    }

    private IMoInflAffixTemplate CreateTemplate(IPartOfSpeech category,
        IReadOnlyList<IMoInflAffixSlot> prefixes, IReadOnlyList<IMoInflAffixSlot> suffixes)
    {
        IMoInflAffixTemplate template = null!;
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
        {
            template = _cache.ServiceLocator.GetInstance<IMoInflAffixTemplateFactory>().Create();
            category.AffixTemplatesOS.Add(template);
            template.Name.set_String(_cache.DefaultAnalWs, "Verb template");
            foreach (var slot in prefixes) template.PrefixSlotsRS.Add(slot);
            foreach (var slot in suffixes) template.SuffixSlotsRS.Add(slot);
        });
        return template;
    }

    private IMoInflAffMsa CreateInflectionalMsa(Guid entryId, IPartOfSpeech category)
    {
        IMoInflAffMsa msa = null!;
        var entry = _cache.ServiceLocator.GetInstance<ILexEntryRepository>().GetObject(entryId);
        NonUndoableUnitOfWorkHelper.Do(_cache.ActionHandlerAccessor, () =>
            msa = _cache.ServiceLocator.GetInstance<IMoInflAffMsaFactory>().Create(
                entry, SandboxGenericMSA.Create(MsaType.kInfl, category)));
        return msa;
    }

    private Proposal Proposal(IReadOnlyList<OperationEnvelope> operations) => new(
        new Dictionary<string, string> { ["grammar"] = "1.0" }, CanonicalId.Mint(), null, operations);

    private static CanonicalId Id(ICmObject value) => CanonicalId.FromGuid(value.Guid);
}
