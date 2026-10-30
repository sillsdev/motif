using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Runner.Execution;

namespace SIL.Motif.Runner.Operations;

/// <summary>
/// The slots a Draft's operations already occupy, built once so each added operation is checked in time that does
/// not grow with the Draft. It reports the same conflicts, naming the same pair, as checking the added operation
/// against every earlier operation in order.
/// </summary>
public sealed class ProposalSlotIndex
{
    private readonly Dictionary<OperationSlot, (OperationEnvelope Operation, int Order)> _exact = new();
    private readonly Dictionary<CanonicalId, (OperationEnvelope Operation, int Order)> _targets = new();
    private readonly Dictionary<CanonicalId, (OperationEnvelope Operation, int Order)> _deletions = new();
    private readonly Dictionary<(CanonicalId Target, string Field), (OperationEnvelope Operation, int Order)> _fields = new();
    private readonly Dictionary<(CanonicalId Target, string Field), (OperationEnvelope Operation, int Order)> _wholeFields = new();
    private readonly List<OperationEnvelope> _all = new();
    private bool _hasWordformCreate;

    /// <summary>Indexes the retained operations; conflicts among them are not reported.</summary>
    public ProposalSlotIndex(IReadOnlyList<OperationEnvelope> retained)
    {
        foreach (var operation in retained) Insert(operation, OperationSlotRegistry.Resolve(operation));
    }

    /// <summary>
    /// Throws on a conflict that involves an added operation, then holds the added operations for later calls.
    /// </summary>
    /// <exception cref="ConflictRefusal">An added operation addresses the same slot as an earlier one.</exception>
    public void Append(IReadOnlyList<OperationEnvelope> added)
    {
        foreach (var operation in added)
        {
            var slots = OperationSlotRegistry.Resolve(operation);
            if (FindEarliest(slots) is { } existing)
                throw new ConflictRefusal(existing.OperationId, operation.OperationId);
            Insert(operation, slots);
        }
        if (_hasWordformCreate)
            TextCreateDependencies.Validate(_all, added);
    }

    private OperationEnvelope? FindEarliest(IReadOnlyList<OperationSlot> slots)
    {
        (OperationEnvelope Operation, int Order)? best = null;
        void Consider((OperationEnvelope Operation, int Order) hit)
        {
            if (best is null || hit.Order < best.Value.Order) best = hit;
        }
        foreach (var slot in slots)
        {
            if (_deletions.TryGetValue(slot.Target, out var hit)) Consider(hit);
            if (slot.Field is null)
            {
                if (_targets.TryGetValue(slot.Target, out hit)) Consider(hit);
            }
            else
            {
                var field = (slot.Target, slot.Field);
                if (_wholeFields.TryGetValue(field, out hit)) Consider(hit);
                if (slot.WholeField && _fields.TryGetValue(field, out hit)) Consider(hit);
                if (_exact.TryGetValue(slot.Key, out hit)) Consider(hit);
            }
        }
        return best?.Operation;
    }

    private void Insert(OperationEnvelope operation, IReadOnlyList<OperationSlot> slots)
    {
        var entry = (operation, _all.Count);
        _all.Add(operation);
        if (operation.Kind == LangProjectWordformsOperationKinds.Create && operation.EntityId is not null)
            _hasWordformCreate = true;
        foreach (var slot in slots)
        {
            if (!slot.ReleasesMember) _targets.TryAdd(slot.Target, entry);
            if (slot.Field is null)
                _deletions.TryAdd(slot.Target, entry);
            else
            {
                _fields.TryAdd((slot.Target, slot.Field), entry);
                if (slot.WholeField) _wholeFields.TryAdd((slot.Target, slot.Field), entry);
                _exact.TryAdd(slot.Key, entry);
            }
        }
    }
}
