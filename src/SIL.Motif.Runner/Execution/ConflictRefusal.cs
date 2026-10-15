using System.Collections.Generic;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Runner.Execution;

/// <summary>Two operations of one Proposal address the same target, field and member slot.</summary>
public sealed class ConflictRefusal(CanonicalId existingOperationId, CanonicalId duplicateOperationId)
    : ProposalRefusalException(
        RefusalCodes.DraftInvalid,
        FailureReason.Refused,
        $"Operations {existingOperationId.Value} and {duplicateOperationId.Value} address the same slot. " +
        "Remove one with remove-operations, or use the composer that replaces an earlier edit.",
        new Dictionary<string, string>
        {
            ["existingOperationId"] = existingOperationId.Value,
            ["duplicateOperationId"] = duplicateOperationId.Value,
        })
{
    /// <summary>The operation already in the Draft.</summary>
    public CanonicalId ExistingOperationId { get; } = existingOperationId;

    /// <summary>The operation being added that collides with it.</summary>
    public CanonicalId DuplicateOperationId { get; } = duplicateOperationId;
}
