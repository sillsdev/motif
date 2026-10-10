using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds explicit, identity-anchored order edits for an existing affix template.</summary>
public static class EditAffixTemplateComposer
{
    private const string ConstructName = "EditAffixTemplate";
    private const string Rationale = "Authored by the EditAffixTemplate composer.";

    /// <summary>Requires exact expected sequences and emits only moves of existing slots.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditAffixTemplateIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var template = ReferenceFieldLowering.Resolve<IMoInflAffixTemplate>(cache, intent.Target, ConstructName);
        var currentPrefix = template.PrefixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid)).ToArray();
        var currentSuffix = template.SuffixSlotsRS.Select(slot => CanonicalId.FromGuid(slot.Guid)).ToArray();
        RequireExpected(intent.Target, "prefix", intent.ExpectedPrefixSlots, currentPrefix);
        RequireExpected(intent.Target, "suffix", intent.ExpectedSuffixSlots, currentSuffix);
        RequireSameMembers(intent.Target, "prefix", currentPrefix, intent.PrefixSlots);
        RequireSameMembers(intent.Target, "suffix", currentSuffix, intent.SuffixSlots);

        return Edits(intent.Target, currentPrefix, intent.PrefixSlots,
                MoInflAffixTemplatePrefixSlotsOperationKinds.MovePrefixSlots)
            .Concat(Edits(intent.Target, currentSuffix, intent.SuffixSlots,
                MoInflAffixTemplateSuffixSlotsOperationKinds.MoveSuffixSlots))
            .ToArray();
    }

    private static IEnumerable<OperationEnvelope> Edits(CanonicalId target, IReadOnlyList<CanonicalId> current,
        IReadOnlyList<CanonicalId> desired, string moveKind) =>
        OrderedSequenceDiff.Create(current, desired).Select(edit =>
        {
            if (edit.Kind != OrderedSequenceEditKind.Move || edit.Placement is null)
                throw new InvalidOperationException("Template edits may only move existing slots.");
            return new OperationEnvelope(CanonicalId.Mint(), moveKind,
                target: target,
                after: JsonSerializer.SerializeToElement(new { member = edit.Member.Value }),
                placement: edit.Placement,
                rationale: Rationale);
        });

    private static void RequireExpected(CanonicalId target, string side,
        IReadOnlyList<CanonicalId> expected, IReadOnlyList<CanonicalId> current)
    {
        if (!expected.SequenceEqual(current))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{target.Value}' {side} slots differ from the expected order.");
    }

    private static void RequireSameMembers(CanonicalId target, string side,
        IReadOnlyList<CanonicalId> current, IReadOnlyList<CanonicalId> desired)
    {
        if (current.Count != desired.Count || !current.ToHashSet().SetEquals(desired))
            throw new InvalidOperationException(
                $"'{ConstructName}': {side} order may only reorder the slots already used by target '{target.Value}'.");
    }
}
