using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds explicit, identity-anchored membership and order edits for an existing affix template.</summary>
public static class EditAffixTemplateComposer
{
    private const string ConstructName = "EditAffixTemplate";
    private const string Rationale = "Authored by the EditAffixTemplate composer.";

    /// <summary>Requires exact expected sequences, then adds, removes and moves slots to reach the requested ones.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAffixTemplateIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var template = ReferenceFieldLowering.Resolve<IMoInflAffixTemplate>(cache, intent.Target, ConstructName);
        var currentPrefix = template.PrefixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid)).ToArray();
        var currentSuffix = template.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid)).ToArray();
        RequireExpected(intent.Target, "prefix", intent.ExpectedPrefixSlots, currentPrefix);
        RequireExpected(intent.Target, "suffix", intent.ExpectedSuffixSlots, currentSuffix);
        RequireUnique(intent.Target, "prefix", intent.PrefixSlots);
        RequireUnique(intent.Target, "suffix", intent.SuffixSlots);

        return Edits(intent.Target, currentPrefix, intent.PrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.AddRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.RemoveRefPrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots)
            .Concat(Edits(intent.Target, currentSuffix, intent.SuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.AddRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.RemoveRefSuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots))
            .ToArray();
    }

    private static IEnumerable<OperationEnvelope> Edits(CanonicalId target, IReadOnlyList<CanonicalId> current,
        IReadOnlyList<CanonicalId> desired, string addKind, string removeKind, string moveKind) =>
        OrderedSequenceDiff.Create(current, desired).Select(edit => new OperationEnvelope(CanonicalId.Mint(),
            edit.Kind switch
            {
                OrderedSequenceEditKind.Add => addKind,
                OrderedSequenceEditKind.Remove => removeKind,
                OrderedSequenceEditKind.Move => moveKind,
                _ => throw new InvalidOperationException("Unsupported template slot edit."),
            },
            target: target,
            after: JsonSerializer.SerializeToElement(new { member = edit.Member.Value }),
            placement: edit.Kind == OrderedSequenceEditKind.Remove ? null : edit.Placement,
            rationale: Rationale));

    private static void RequireExpected(CanonicalId target, string side,
        IReadOnlyList<CanonicalId> expected, IReadOnlyList<CanonicalId> current)
    {
        if (!expected.SequenceEqual(current))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{target.Value}' {side} slots differ from the expected order.");
    }

    private static void RequireUnique(CanonicalId target, string side, IReadOnlyList<CanonicalId> desired)
    {
        if (desired.Distinct(CanonicalId.ByGuid).Count() != desired.Count)
            throw new InvalidOperationException(
                $"'{ConstructName}': {side} slots of target '{target.Value}' must contain unique identities.");
    }
}
