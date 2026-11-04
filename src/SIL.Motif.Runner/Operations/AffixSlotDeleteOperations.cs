using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Operations;

/// <summary>Names the operation that takes one unused inflectional affix slot out of its part of speech.</summary>
public static class AffixSlotDeleteOperationKinds
{
    /// <summary>Deletes one affix slot.</summary>
    public const string DeleteAffixSlots = "grammar/partOfSpeech/deleteAffixSlots";

#pragma warning disable CA2255 // Load-time registration makes this kind available before parsing and dispatch.
    [ModuleInitializer]
    internal static void Register()
    {
        OperationKindRegistry.Register(DeleteAffixSlots);
        OperationHandlerRegistry.Register(DeleteAffixSlots, new AffixSlotDeleteHandler());
        OperationSlotRegistry.RegisterEntityDeletion(DeleteAffixSlots);
    }
#pragma warning restore CA2255
}

/// <summary>Lists what still uses an affix slot, for the composer and the handler, so neither cascades.</summary>
public static class AffixSlotUse
{
    /// <summary>Returns the inflectional affixes and templates that name the slot, never the slot itself.</summary>
    public static IReadOnlyList<ICmObject> Users(IMoInflAffixSlot slot) =>
        slot.ReferringObjects.Where(user => !ReferenceEquals(user, slot)).ToArray();

    /// <summary>Throws a message that names what uses the slot, so the linguist knows what to move first.</summary>
    public static void RequireUnused(IMoInflAffixSlot slot, CanonicalId id, string kind)
    {
        var users = Users(slot);
        if (users.Count == 0) return;
        var msas = users.OfType<IMoInflAffMsa>().Count();
        var templates = users.OfType<IMoInflAffixTemplate>().Count();
        var others = users.Count - msas - templates;
        var parts = new List<string>();
        if (msas > 0) parts.Add($"{msas} inflectional affix analys{(msas == 1 ? "is" : "es")}");
        if (templates > 0) parts.Add($"{templates} affix template{(templates == 1 ? string.Empty : "s")}");
        if (others > 0) parts.Add($"{others} other object{(others == 1 ? string.Empty : "s")}");
        throw new InvalidOperationException(
            $"'{kind}': slot '{id.Value}' is still used by {string.Join(" and ", parts)} " +
            $"(first: a {users[0].ClassName}, {users[0].Guid:D}); move those affixes to another slot with " +
            "compose-edit-inflectional-affix and take the slot out of its templates with " +
            "compose-edit-affix-template first.");
    }
}

/// <summary>Resolves, checks, deletes and re-reads one affix slot.</summary>
internal sealed class AffixSlotDeleteHandler : IOperationHandler
{
    private const string Kind = AffixSlotDeleteOperationKinds.DeleteAffixSlots;

    public ExpectedEffect ApplyAndCaptureEffect(LcmCache cache, OperationEnvelope operation, List<CanonicalId> touchedTargets)
    {
        RequireEmptyPayload(operation);
        var (id, slot) = Resolve(cache, operation);
        touchedTargets.Add(id);
        AffixSlotUse.RequireUnused(slot, id, Kind);
        var before = ReferenceFieldAlternatives.ToAlternatives(id);
        slot.Delete();
        return new ExpectedEffect(id, SnapshotFields.PartOfSpeechAffixSlots, before,
            ReferenceFieldAlternatives.ToAlternatives(null));
    }

    public ExpectedEffect ReadCurrentFootprint(LcmCache cache, OperationEnvelope operation)
    {
        var (id, _) = Resolve(cache, operation);
        var current = ReferenceFieldAlternatives.ToAlternatives(id);
        return new ExpectedEffect(id, SnapshotFields.PartOfSpeechAffixSlots, current, current);
    }

    private static (CanonicalId Id, IMoInflAffixSlot Slot) Resolve(LcmCache cache, OperationEnvelope operation)
    {
        var (id, slot) = TargetResolution.Resolve<IMoInflAffixSlot>(cache, operation, Kind);
        if (slot.Owner is not IPartOfSpeech)
            throw new InvalidOperationException($"'{Kind}': '{id.Value}' is not a slot of a part of speech.");
        return (id, slot);
    }

    private static void RequireEmptyPayload(OperationEnvelope operation)
    {
        if (operation.After is not { } after)
            throw new InvalidOperationException(
                $"Operation '{operation.OperationId.Value}' of kind '{Kind}' requires 'after'.");
        FsFeatStrucFeatureSpecsPayload.Parse(after, Kind);
    }
}
