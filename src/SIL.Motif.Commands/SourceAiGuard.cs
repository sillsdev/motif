using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands;

/// <summary>
/// Keeps an AI's Source edits in a named Proposal. The pending changes are a person's Save; an AI edit of a Source is
/// a composer step in a Draft from <c>motif_start_proposal</c>, Finalized for a person to review and Apply.
/// </summary>
public static class SourceAiGuard
{
    /// <summary>Whether <paramref name="verb"/> is one of the five composers that edit the Motif Sources list.</summary>
    public static bool IsSourceVerb(string verb) =>
        verb == "compose-add-source" || verb.StartsWith("compose-set-source-", StringComparison.Ordinal);

    /// <summary>
    /// The refusal for an AI step that names a Source composer and the reserved pending-changes Draft, or
    /// <see langword="null"/> when the step may go on.
    /// </summary>
    public static Refusal? Check(string? draftName, string verb) =>
        draftName is not null && IsSourceVerb(verb) &&
        string.Equals(draftName.Trim(), PendingChanges.DraftName, StringComparison.OrdinalIgnoreCase)
            ? new Refusal(RefusalCodes.SourceAiNeedsProposal, FailureReason.Refused,
                $"'{PendingChanges.DraftName}' holds a person's own changes. Start a Proposal with motif_start_proposal and " +
                "compose Source edits in that Draft, then Finalize it for a person to review and Apply.",
                new Dictionary<string, string> { ["draft"] = PendingChanges.DraftName, ["verb"] = verb })
            : null;
}
