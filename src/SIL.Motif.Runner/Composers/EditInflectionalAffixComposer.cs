using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds membership edits for an existing inflectional affix MSA.</summary>
public static class EditInflectionalAffixComposer
{
    private const string ConstructName = "EditInflectionalAffix";
    private const string Rationale = "Authored by the EditInflectionalAffix composer.";

    /// <summary>Checks the MSA subtype, category and expected set before emitting reference operations.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditInflectionalAffixIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var target = ReferenceFieldLowering.Resolve<ICmObject>(cache, intent.Target, ConstructName);
        if (target is not IMoInflAffMsa msa)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' is not an existing inflectional affix MSA.");

        var current = msa.SlotsRC.Select(slot => CanonicalId.FromGuid(slot.Guid))
            .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (!current.SequenceEqual(intent.ExpectedSlots))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' slot memberships differ from the expected set.");

        var category = msa.PartOfSpeechRA ?? throw new InvalidOperationException(
            $"'{ConstructName}': target '{intent.Target.Value}' has no affix category.");
        foreach (var id in intent.Slots)
        {
            var slot = ReferenceFieldLowering.Resolve<IMoInflAffixSlot>(cache, id, ConstructName);
            if (!BelongsToCategoryOrAncestor(slot, category))
                throw new InvalidOperationException(
                    $"'{ConstructName}': slot '{id.Value}' does not belong to the affix category or one of its ancestors.");
        }

        var desired = intent.Slots.ToHashSet();
        var existing = current.ToHashSet();
        var removed = existing.Except(desired).OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => Operation(MoInflAffMsaSlotsOperationKinds.RemoveRefSlots, intent.Target, id));
        var added = desired.Except(existing).OrderBy(id => id.Value, StringComparer.Ordinal)
            .Select(id => Operation(MoInflAffMsaSlotsOperationKinds.AddRefSlots, intent.Target, id));
        return removed.Concat(added).ToArray();
    }

    internal static OperationEnvelope BuildAssignmentForCreatedSlot(LcmCache cache, CanonicalId targetId,
        CanonicalId slotId, IPartOfSpeech slotCategory, CanonicalId createOperationId, Func<CanonicalId> mintId)
    {
        var target = ReferenceFieldLowering.Resolve<ICmObject>(cache, targetId, ConstructName);
        if (target is not IMoInflAffMsa msa)
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{targetId.Value}' is not an existing inflectional affix MSA.");

        var category = msa.PartOfSpeechRA ?? throw new InvalidOperationException(
            $"'{ConstructName}': target '{targetId.Value}' has no affix category.");
        if (!BelongsToCategoryOrAncestor(slotCategory, category))
            throw new InvalidOperationException(
                $"'{ConstructName}': slot category '{slotCategory.Guid}' is not the affix category or one of its ancestors.");

        return new OperationEnvelope(mintId(), MoInflAffMsaSlotsOperationKinds.AddRefSlots,
            target: targetId, after: JsonSerializer.SerializeToElement(new { member = slotId.Value }),
            dependsOn: [new OperationDependency(createOperationId)], rationale: Rationale);
    }

    private static bool BelongsToCategoryOrAncestor(IMoInflAffixSlot slot, IPartOfSpeech category)
    {
        var owner = slot.Owner as IPartOfSpeech;
        return owner is not null && BelongsToCategoryOrAncestor(owner, category);
    }

    private static bool BelongsToCategoryOrAncestor(IPartOfSpeech slotCategory, IPartOfSpeech category)
    {
        for (var current = category; current is not null; current = current.Owner as IPartOfSpeech)
            if (slotCategory.Guid == current.Guid) return true;
        return false;
    }

    private static OperationEnvelope Operation(string kind, CanonicalId target, CanonicalId slot) =>
        new(CanonicalId.Mint(), kind, target: target,
            after: JsonSerializer.SerializeToElement(new { member = slot.Value }), rationale: Rationale);
}
