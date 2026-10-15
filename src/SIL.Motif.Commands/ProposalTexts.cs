using System.Text;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>The Proposal a selector named and whether the person's note is newer than its Rationale.</summary>
public sealed record NoteFinding(string ProposalId, bool NoteChanged);

/// <summary>
/// A Proposal's two Markdown texts. The Rationale says what the Proposal changes and why, and whoever builds the
/// Proposal writes it. The note is the person's reply, and only a person writes it. Each is stamped with the
/// intent digest it was written for, and neither enters that digest, so writing either never changes what the
/// Proposal does.
/// </summary>
public static class ProposalTexts
{
    /// <summary>The largest text, in UTF-8 bytes, either field holds.</summary>
    public const int MaxBytes = 64 * 1024;

    /// <summary>Writes a Proposal's Rationale, replacing the one there; allowed until the Proposal is closed.</summary>
    public static CommandOutcome<ProposalTextChangedResponse> SetRationale(SetRationaleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(request.FwDataPath, request.ProductVersion, request.ProposalId, "rationale", request.Text,
            requireText: true);
    }

    /// <summary>Writes, or with a null text clears, the person's note on a Proposal.</summary>
    public static CommandOutcome<ProposalTextChangedResponse> SetNote(SetNoteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Write(request.FwDataPath, request.ProductVersion, request.ProposalId, "note", request.Text,
            requireText: false);
    }

    /// <summary>
    /// Whether the person wrote a note after the Rationale was last written, for the Proposal a full id or a Draft
    /// name selects; null when the selector names no Proposal.
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
                ProposalDetailProjectionBuilder.NoteIsNewer(ProposalRecordMapping.ToManifest(record))));
        });
        return outcome.Succeeded ? outcome.Value : null;
    }

    private static CommandOutcome<ProposalTextChangedResponse> Write(string fwDataPath, string productVersion,
        string proposalId, string field, string? text, bool requireText)
    {
        return ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(proposalId, out var id))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.not-found",
                    FailureReason.NotFound, $"'{proposalId}' is not a full Proposal id. Use the proposalId a Proposal " +
                    "listing returns; prefixes and names are not accepted.", proposalId));
            if (requireText && string.IsNullOrWhiteSpace(text))
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.rationale-empty",
                    FailureReason.InvalidArgument, "A Rationale cannot be empty. Say what the Proposal changes and why.",
                    id.Value));
            if (text is not null && Encoding.UTF8.GetByteCount(text) > MaxBytes)
                return CommandOutcome<ProposalTextChangedResponse>.Refused(Refuse("proposal.text-too-large",
                    FailureReason.InvalidArgument, $"The {field} is larger than {MaxBytes / 1024} KiB. Shorten it.",
                    id.Value));
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
                    FailureReason.Refused, $"Proposal '{id.Value}' is {record.Status}, so its {field} can no longer change.",
                    id.Value));

            if (field == "rationale") repository.SetRationale(id, text);
            else repository.SetNote(id, text);
            var written = repository.Get(id);
            var manifest = ProposalRecordMapping.ToManifest(written);
            return CommandOutcome<ProposalTextChangedResponse>.Success(new ProposalTextChangedResponse(
                id.Value, field, field == "rationale" ? written.RationaleUpdatedUtc : written.NoteUpdatedUtc,
                ProposalDetailProjectionBuilder.NoteIsNewer(manifest)));
        });
    }

    private static Refusal Refuse(string code, FailureReason reason, string message, string proposalId) =>
        new(code, reason, message, new Dictionary<string, string>(StringComparer.Ordinal) { ["proposalId"] = proposalId });
}
