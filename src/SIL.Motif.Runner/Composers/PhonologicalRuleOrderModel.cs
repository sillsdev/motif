using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>Plans identity-relative moves within one phonological rule sequence.</summary>
public static class PhonologicalRuleOrderModel
{
    /// <summary>
    /// Creates the minimal move operations that transform one rule order into another while retaining
    /// exactly the same rule identities.
    /// </summary>
    /// <param name="phonologicalData">Identity of the owning phonological data object.</param>
    /// <param name="currentOrder">Rule identities in their current order.</param>
    /// <param name="desiredOrder">The same rule identities in the requested order.</param>
    /// <param name="mintOperationId">Optional source of operation identities.</param>
    /// <remarks>
    /// The member sets must be equal because this model represents moves only. Every move depends on
    /// the move before it, making the computed placements safe even when array order carries no meaning.
    /// </remarks>
    public static IReadOnlyList<OperationEnvelope> PlanMoves(CanonicalId phonologicalData,
        IReadOnlyList<CanonicalId> currentOrder, IReadOnlyList<CanonicalId> desiredOrder,
        Func<CanonicalId>? mintOperationId = null)
    {
        ArgumentNullException.ThrowIfNull(currentOrder);
        ArgumentNullException.ThrowIfNull(desiredOrder);
        var edits = OrderedSequenceDiff.Create(currentOrder, desiredOrder);
        if (edits.Any(edit => edit.Kind != OrderedSequenceEditKind.Move))
            throw new InvalidOperationException("A rule-order move must retain the same rule identities.");

        var mint = mintOperationId ?? (() => CanonicalId.Mint());
        var operations = new List<OperationEnvelope>(edits.Count);
        CanonicalId? previousOperation = null;
        foreach (var edit in edits)
        {
            var operationId = mint();
            operations.Add(new OperationEnvelope(operationId, PhPhonDataPhonRulesOperationKinds.MovePhonRules,
                target: phonologicalData,
                after: JsonSerializer.SerializeToElement(new { member = edit.Member.Value }),
                placement: edit.Placement ?? throw new InvalidOperationException(
                    "A rule-order move requires an identity-relative placement."),
                dependsOn: previousOperation is { } previous
                    ? [new OperationDependency(previous)]
                    : null));
            previousOperation = operationId;
        }
        return operations;
    }
}
