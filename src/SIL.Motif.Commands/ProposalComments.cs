using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The Comments on a Proposal. An agent may add one, with a label it chose for itself, and never edits or deletes
/// one; a person adds, edits and deletes any. The label is shown to readers and is no identity check. Comments
/// freeze when the Proposal is decided, as the Rationale does.
/// </summary>
/// <remarks>
/// Edit and delete take no author because only a person may call them; an agent-facing surface registers them
/// as human-only.
/// </remarks>
public static class ProposalComments
{
    /// <summary>Adds a Comment to an open Proposal.</summary>
    public static CommandOutcome<ProposalCommentChangedResponse> Add(AddCommentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (discussion, id) =>
        {
            if (string.IsNullOrWhiteSpace(request.Text))
                return Refusal(ProposalTexts.Refuse(RefusalCodes.ProposalCommentInvalid, FailureReason.InvalidArgument,
                    "A Comment cannot be empty.", id.Value));
            if (AuthorProblem(request.AuthorLabel, request.PersonName) is { } problem)
                return Refusal(ProposalTexts.Refuse(RefusalCodes.ProposalCommentInvalid, FailureReason.InvalidArgument,
                    problem, id.Value));
            return Result(id, discussion.AddComment(id, request.Text,
                new DiscussionAuthor(request.AuthorLabel, request.PersonName ?? "")), "added");
        });
    }

    /// <summary>Replaces the text of a Comment; it then shows that it was edited, and no earlier text is kept.</summary>
    public static CommandOutcome<ProposalCommentChangedResponse> Edit(EditCommentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (discussion, id) =>
            string.IsNullOrWhiteSpace(request.Text)
                ? Refusal(ProposalTexts.Refuse(RefusalCodes.ProposalCommentInvalid, FailureReason.InvalidArgument,
                    "A Comment cannot be empty. Delete it instead.", id.Value))
                : Result(id, discussion.EditComment(id, request.CommentId, request.Text), "edited"));
    }

    /// <summary>Deletes a Comment and the pictures attached to it.</summary>
    public static CommandOutcome<ProposalCommentChangedResponse> Delete(DeleteCommentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (discussion, id) =>
            Result(id, discussion.DeleteComment(id, request.CommentId), "deleted"));
    }

    internal static CommandOutcome<ProposalCommentChangedResponse> RemovePersonComments(SetNoteRequest request) =>
        Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (discussion, id) =>
            Result(id, discussion.DeletePersonComments(id), "deleted"));

    /// <summary>Why an author label or person name is unacceptable, or null when both are.</summary>
    internal static string? AuthorProblem(string? authorLabel, string? personName)
    {
        if (authorLabel is not null && string.IsNullOrWhiteSpace(authorLabel))
            return "The author label cannot be blank. Leave it out when a person writes.";
        if (authorLabel is { Length: > ProposalTexts.MaxAuthorLength } ||
            personName is { Length: > ProposalTexts.MaxAuthorLength })
            return $"An author label or person name can be at most {ProposalTexts.MaxAuthorLength} characters.";
        return null;
    }

    private delegate CommandOutcome<ProposalCommentChangedResponse> Step(
        ProposalDiscussionRepository discussion, CanonicalId id);

    private static CommandOutcome<ProposalCommentChangedResponse> Run(string fwDataPath, string productVersion,
        string proposalId, Step step) =>
        ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(proposalId, out var id))
                return Refusal(ProposalTexts.Refuse("proposal.not-found", FailureReason.NotFound,
                    $"'{proposalId}' is not a full Proposal id. Use the proposalId a Proposal listing returns; " +
                    "prefixes and names are not accepted.", proposalId));
            var outcome = step(new ProposalDiscussionRepository(database), id);
            if (!outcome.Succeeded) return outcome;
            var record = new ProposalRepository(database).Get(id);
            return CommandOutcome<ProposalCommentChangedResponse>.Success(
                outcome.Value! with { NoteChanged = ProposalTexts.NoteChanged(database, record) });
        });

    private static CommandOutcome<ProposalCommentChangedResponse> Result(CanonicalId id, CommentWrite write,
        string change) => write.Outcome switch
    {
        CommentWriteOutcome.Written => CommandOutcome<ProposalCommentChangedResponse>.Success(
            new ProposalCommentChangedResponse(id.Value, write.CommentId, change, false)),
        CommentWriteOutcome.Closed => Refusal(ProposalTexts.Refuse("proposal.closed", FailureReason.Refused,
            $"Proposal '{id.Value}' is decided, so its Comments can no longer change.", id.Value)),
        CommentWriteOutcome.CommentNotFound => Refusal(ProposalTexts.Refuse("proposal.not-found",
            FailureReason.NotFound, $"Proposal '{id.Value}' has no Comment {write.CommentId}.", id.Value)),
        CommentWriteOutcome.ProposalNotFound => Refusal(ProposalTexts.Refuse("proposal.not-found",
            FailureReason.NotFound, $"Proposal '{id.Value}' was not found.", id.Value)),
        _ => throw new InvalidOperationException($"Unhandled Comment outcome {write.Outcome}."),
    };

    private static CommandOutcome<ProposalCommentChangedResponse> Refusal(Refusal refusal) =>
        CommandOutcome<ProposalCommentChangedResponse>.Refused(refusal);
}
