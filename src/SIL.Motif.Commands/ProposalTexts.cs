using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The Proposal a selector named and whether a person wrote after the builder last did.</summary>
public sealed record NoteFinding(string ProposalId, bool NoteChanged);

/// <summary>
/// A Proposal's Rationale. It says what the Proposal changes and why, and whoever builds the Proposal writes it;
/// people and agents may rewrite it while the Proposal is open, and each rewrite is kept as a revision until the
/// decision. It is stamped with the intent digest it was written for and does not enter that digest, so writing it
/// never changes what the Proposal does. Comments, the person's replies, are in <see cref="ProposalComments"/>.
/// </summary>
public static class ProposalTexts
{
    /// <summary>The longest author label or person name a revision or Comment may carry.</summary>
    public const int MaxAuthorLength = 100;

    /// <summary>Writes a Proposal's Rationale, replacing the one there; allowed until the Proposal is closed.</summary>
    public static CommandOutcome<ProposalTextChangedResponse> SetRationale(SetRationaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(request.FwDataPath, request.ProductVersion, request.ProposalId, request.Text,
            request.AuthorLabel, request.PersonName);
    }

    /// <summary>
    /// Adds a Comment written by the person, or with a null text removes the Comments the person wrote on a Proposal.
    /// </summary>
    public static CommandOutcome<ProposalTextChangedResponse> SetNote(SetNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Text is not null)
        {
            var added = ProposalComments.Add(new AddCommentRequest(request.FwDataPath, request.ProductVersion,
                request.ProposalId, request.Text, Environment.UserName));
            return added.Succeeded
                ? CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                    added.Value!.ProposalId, "note", StoredTime(request, added.Value.CommentId),
                    added.Value.NoteChanged))
                : CommandOutcome<ProposalTextChangedResponse>.Refused(added.Refusal!);
        }
        var cleared = ProposalComments.RemovePersonComments(request);
        return cleared.Succeeded
            ? CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                cleared.Value!.ProposalId, "note", null, cleared.Value.NoteChanged))
            : CommandOutcome<ProposalTextChangedResponse>.Refused(cleared.Refusal!);
    }

    // The time the store kept for a Comment, so the legacy response reports what a reader will see.
    private static string? StoredTime(SetNoteRequest request, long commentId)
    {
        var outcome = ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(request.ProposalId, out var id)) return CommandOutcome<string?>.Success(null);
            var stored = new ProposalDiscussionRepository(database).ListComments(id)
                .FirstOrDefault(comment => comment.CommentId == commentId)?.CreatedUtc;
            return CommandOutcome<string?>.Success(stored);
        });
        return outcome.Succeeded ? outcome.Value : null;
    }

    /// <summary>
    /// Whether a person wrote after the builder last did, for the Proposal a full id or a Draft name selects; null
    /// when the selector names no Proposal.
    /// </summary>
    public static NoteFinding? NoteChangedFor(
        string fwDataPath, string productVersion, string selector)
    {
        var outcome = ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                record = CanonicalId.TryParse(selector, out var id)
                    ? repository.Get(id)
                    : repository.DraftNameExists(selector) ? repository.GetDraft(selector)
                    : throw new KeyNotFoundException(selector);
            }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<NoteFinding?>.Success(null);
            }
            return CommandOutcome<NoteFinding?>.Success(new NoteFinding(record.ProposalId.Value,
                NoteChanged(database, record)));
        });
        return outcome.Succeeded ? outcome.Value : null;
    }

    internal static bool NoteChanged(MotifDatabase database, ProposalRecord record) =>
        ProposalDetailProjectionBuilder.PersonCommentIsNewer(ProposalRecordMapping.ToManifest(record,
            new ProposalDiscussionRepository(database).ListComments(record.ProposalId)));

    private static CommandOutcome<ProposalTextChangedResponse> Write(string fwDataPath, string productVersion,
        string proposalId, string text, string? authorLabel, string? personName)
    {
        return ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(proposalId, out var id))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"'{proposalId}' is not a full Proposal id. Use the proposalId a Proposal " +
                    "listing returns; prefixes and names are not accepted.", proposalId));
            if (string.IsNullOrWhiteSpace(text))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.rationale-empty",
                    FailureReason.InvalidArgument, "A Rationale cannot be empty. Say what the Proposal changes and why.",
                    id.Value));
            if (ProposalComments.AuthorProblem(authorLabel, personName) is { } problem)
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse(RefusalCodes.ProposalCommentInvalid,
                    FailureReason.InvalidArgument, problem, id.Value));
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try { record = repository.Get(id); }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"Proposal '{id.Value}' was not found.", id.Value));
            }
            if (record.DraftName is null && ProposalLifecycle.IsTerminal(record.Status))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.closed",
                    FailureReason.Refused, $"Proposal '{id.Value}' is {record.Status}, so its rationale can no longer change.",
                    id.Value));

            repository.SetRationale(id, text, new DiscussionAuthor(authorLabel, personName ?? ""));
            var written = repository.Get(id);
            return CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                id.Value, "rationale", written.RationaleUpdatedUtc, NoteChanged(database, written)));
        });
    }

    internal static Refusal Refuse(string code, FailureReason reason, string message, string proposalId) =>
        new(code, reason, message, new Dictionary<string, string>(StringComparer.Ordinal) { ["proposalId"] = proposalId });
}
