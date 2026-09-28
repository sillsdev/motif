using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

internal static class PendingChangeRefusals
{
    public static Refusal Uncertain(IEnumerable<string> changeIds, string? proposalId = null)
    {
        var changes = string.Join(", ", changeIds);
        var message = proposalId is null
            ? $"Change {changes} is uncertain. Check again before applying."
            : $"Cannot apply Proposal {proposalId}: change {changes} is uncertain. Check again before applying.";
        IReadOnlyDictionary<string, string>? facts = proposalId is null
            ? null
            : new Dictionary<string, string> { ["proposalId"] = proposalId };
        return new Refusal("apply.change-uncertain", FailureReason.Refused, message, facts);
    }
}
