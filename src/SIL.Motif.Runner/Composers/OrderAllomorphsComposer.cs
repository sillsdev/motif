using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Builds identity-anchored moves within one entry's existing alternate-form sequence.</summary>
public static class OrderAllomorphsComposer
{
    private const string ConstructName = "OrderAllomorphs";
    private const string Rationale = "Authored by the OrderAllomorphs composer.";

    /// <summary>Requires exact current order and emits moves without creating, deleting or reparenting forms.</summary>
    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, OrderAllomorphsIntent intent)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(intent.ExpectedAlternates);
        ArgumentNullException.ThrowIfNull(intent.Alternates);
        var entry = ReferenceFieldLowering.Resolve<ILexEntry>(cache, intent.Target, ConstructName);
        var current = entry.AlternateFormsOS.Select(form => CanonicalId.FromGuid(form.Guid)).ToArray();
        var expected = Unique(intent.ExpectedAlternates, "expectedAlternates");
        var requested = Unique(intent.Alternates, "alternates");
        if (!expected.SequenceEqual(current))
            throw new InvalidOperationException(
                $"'{ConstructName}': target '{intent.Target.Value}' alternate forms differ from the expected order.");
        if (entry.LexemeFormOA is { } lexemeForm && requested.Contains(CanonicalId.FromGuid(lexemeForm.Guid)))
            throw new InvalidOperationException("'OrderAllomorphs': the lexeme form is not a movable alternate form.");
        if (current.Length != requested.Length || !current.ToHashSet().SetEquals(requested))
            throw new InvalidOperationException(
                $"'{ConstructName}': requested order may only contain the existing alternate forms of target '{intent.Target.Value}'.");

        return OrderedSequenceDiff.Create(current, requested).Select(edit =>
        {
            if (edit.Kind != OrderedSequenceEditKind.Move || edit.Placement is null)
                throw new InvalidOperationException("Alternate-form edits may only move existing forms.");
            return new OperationEnvelope(CanonicalId.Mint(), LexEntryAlternateFormsOperationKinds.MoveAlternateForms,
                target: intent.Target, after: JsonSerializer.SerializeToElement(new { member = edit.Member.Value }),
                placement: edit.Placement, rationale: Rationale);
        }).ToArray();
    }

    private static CanonicalId[] Unique(IReadOnlyList<CanonicalId> ids, string name)
    {
        if (ids.Distinct().Count() != ids.Count)
            throw new InvalidOperationException($"'{ConstructName}': '{name}' must contain unique form identities.");
        return ids.ToArray();
    }
}
