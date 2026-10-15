using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Runner.Operations;

/// <summary>Two operations of one Proposal address the same target, field and member slot.</summary>
public sealed class ProposalSlotConflictException(CanonicalId existingOperationId, CanonicalId duplicateOperationId)
    : Exception(
        $"Operations {existingOperationId.Value} and {duplicateOperationId.Value} address the same slot. " +
        "Remove one with remove-operations, or use the composer that replaces an earlier edit.")
{
    /// <summary>The operation already in the Draft.</summary>
    public CanonicalId ExistingOperationId { get; } = existingOperationId;

    /// <summary>The operation being added that collides with it.</summary>
    public CanonicalId DuplicateOperationId { get; } = duplicateOperationId;
}
