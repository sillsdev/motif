using System;
using System.Collections.Generic;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Retires one inflectional affix slot that no affix and no template uses any more.</summary>
/// <param name="Slot">The slot to delete.</param>
public sealed record RetireAffixSlotIntent(CanonicalId Slot);

/// <summary>Parses the closed JSON intent for retiring a slot.</summary>
public static class RetireAffixSlotIntentParser
{
    /// <summary>Reads the slot; it is required.</summary>
    public static RetireAffixSlotIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, "RetireAffixSlot", "slot");
        return new(SoundSystemIntentParsing.Id(authored, "slot"));
    }
}

/// <summary>
/// Lowers a slot retirement to one <see cref="AffixSlotDeleteOperationKinds.DeleteAffixSlots"/>. A slot that an
/// inflectional affix analysis or an affix template still names is refused, naming what uses it, so nothing is
/// reassigned or cascaded behind the linguist's back.
/// </summary>
public static class RetireAffixSlotComposer
{
    private const string ConstructName = "RetireAffixSlot";
    private const string Rationale = "Authored by the RetireAffixSlot composer: no affix or template uses the slot.";

    /// <summary>Composes the retirement without mutating the caller's project.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, RetireAffixSlotIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var slot = ReferenceFieldLowering.Resolve<IMoInflAffixSlot>(cache, intent.Slot, ConstructName);
        AffixSlotUse.RequireUnused(slot, intent.Slot, ConstructName);
        return [new(CanonicalId.Mint(), AffixSlotDeleteOperationKinds.DeleteAffixSlots,
            target: intent.Slot, after: JsonSerializer.SerializeToElement(new { }), rationale: Rationale)];
    }
}
