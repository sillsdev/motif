using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>Enforces one operation per addressed field slot in a Proposal.</summary>
public static class ProposalOperationSlotValidator
{
    public static void Validate(Proposal proposal)
    {
        if (FindConflict(proposal.Operations) is { } collision)
            throw new ContractParseException(
                $"Operations {collision.Existing.OperationId.Value} and {collision.Duplicate.OperationId.Value} " +
                "address the same target, field, and member slot.");
        TextCreateDependencies.Validate(proposal.Operations);
    }

    /// <summary>
    /// Runs at the moment a change is added to a Draft, with the operations the Draft would then hold, so a
    /// same-slot conflict is refused when the second change arrives instead of at Finalize.
    /// </summary>
    /// <exception cref="ConflictRefusal">Two operations address the same slot.</exception>
    public static void ValidateAppend(IReadOnlyList<OperationEnvelope> retainedPlusAdded)
    {
        if (FindConflict(retainedPlusAdded) is { } collision)
            throw new ConflictRefusal(collision.Existing.OperationId, collision.Duplicate.OperationId);
        TextCreateDependencies.Validate(retainedPlusAdded);
    }

    public static (OperationEnvelope Existing, OperationEnvelope Duplicate)? FindConflict(
        IEnumerable<OperationEnvelope> operations)
    {
        var exact = new Dictionary<OperationSlot, OperationEnvelope>();
        var targets = new Dictionary<CanonicalId, OperationEnvelope>();
        var deletions = new Dictionary<CanonicalId, OperationEnvelope>();
        var fields = new Dictionary<(CanonicalId Target, string Field), OperationEnvelope>();
        var wholeFields = new Dictionary<(CanonicalId Target, string Field), OperationEnvelope>();
        foreach (var operation in operations)
        {
            var slots = OperationSlotRegistry.Resolve(operation);
            foreach (var slot in slots)
            {
                if (deletions.TryGetValue(slot.Target, out var existing)) return (existing, operation);
                if (slot.Field is null)
                {
                    if (targets.TryGetValue(slot.Target, out existing)) return (existing, operation);
                }
                else
                {
                    var field = (slot.Target, slot.Field);
                    if (wholeFields.TryGetValue(field, out existing)) return (existing, operation);
                    if (slot.WholeField && fields.TryGetValue(field, out existing)) return (existing, operation);
                    if (exact.TryGetValue(slot, out existing)) return (existing, operation);
                }
            }

            // Checking the whole footprint first avoids conflicts between overlapping slots within one operation.
            foreach (var slot in slots)
            {
                targets.TryAdd(slot.Target, operation);
                if (slot.Field is null)
                    deletions.TryAdd(slot.Target, operation);
                else
                {
                    fields.TryAdd((slot.Target, slot.Field), operation);
                    if (slot.WholeField) wholeFields.TryAdd((slot.Target, slot.Field), operation);
                    exact.TryAdd(slot, operation);
                }
            }
        }
        return null;
    }
}
