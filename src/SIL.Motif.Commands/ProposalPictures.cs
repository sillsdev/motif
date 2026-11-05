using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The pictures on a Proposal. Only a person adds or removes one: the window scales a picture to about
/// <see cref="MaxBytes"/> first and these commands re-check the cap and the picture's own header, so the CLI
/// cannot bypass it. Pictures live in the project store, count in its budget and are never pruned.
/// </summary>
public static class ProposalPictures
{
    /// <summary>The most bytes one stored picture may take: 1 MiB.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>Adds a PNG or JPEG picture to an open Proposal, or to one of its Comments.</summary>
    public static CommandOutcome<ProposalPictureChangedResponse> Add(AddPictureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (pictures, id) =>
        {
            if (request.Bytes is not { Length: > 0 })
                return Refuse(id, "A picture cannot be empty.");
            if (request.Bytes.Length > MaxBytes)
                return Refuse(id, $"A picture can be at most {MaxBytes} bytes; scale it down first.");
            if (PictureSniffer.Read(request.Bytes) is not var (mediaType, width, height))
                return Refuse(id, "Only PNG and JPEG pictures can be added.");
            var write = pictures.Add(id, request.CommentId, mediaType, width, height, request.Bytes);
            return write.Outcome switch
            {
                PictureWriteOutcome.TooMany => Refuse(id,
                    $"A Proposal holds at most {ProposalPictureRepository.MaxPicturesPerProposal} pictures."),
                _ => Result(id, write, "added", pictures),
            };
        });
    }

    /// <summary>Removes a picture from an open Proposal.</summary>
    public static CommandOutcome<ProposalPictureChangedResponse> Remove(RemovePictureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (pictures, id) =>
            Result(id, pictures.Remove(id, request.PictureId), "removed", pictures));
    }

    /// <summary>Lists a Proposal's pictures, and returns the bytes of one when it is named.</summary>
    public static CommandOutcome<ProposalPicturesResponse> Show(ShowPicturesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(request.ProposalId, out var id) || !Exists(database, id))
                return CommandOutcome<ProposalPicturesResponse>.Refused(ProposalTexts.Refuse("proposal.not-found",
                    FailureReason.NotFound, $"Proposal '{request.ProposalId}' was not found.", request.ProposalId));
            var pictures = new ProposalPictureRepository(database);
            string? data = null;
            if (request.PictureId is { } pictureId)
            {
                var bytes = pictures.ReadBytes(id, pictureId);
                if (bytes is null)
                    return CommandOutcome<ProposalPicturesResponse>.Refused(ProposalTexts.Refuse("proposal.not-found",
                        FailureReason.NotFound, $"Proposal '{id.Value}' has no picture {pictureId}.", id.Value));
                data = Convert.ToBase64String(bytes);
            }
            return CommandOutcome<ProposalPicturesResponse>.Success(
                new ProposalPicturesResponse(id.Value, pictures.List(id).Select(View).ToArray(), data));
        });
    }

    private static bool Exists(SIL.Motif.Host.Store.MotifDatabase database, CanonicalId id)
    {
        try { new ProposalRepository(database).Get(id); return true; }
        catch (KeyNotFoundException) { return false; }
    }

    private static PictureView View(ProposalPictureRecord record) => new(record.PictureId, record.CommentId,
        record.MediaType, record.Width, record.Height, record.ByteCount, record.CreatedUtc);

    private delegate CommandOutcome<ProposalPictureChangedResponse> Step(ProposalPictureRepository pictures, CanonicalId id);

    private static CommandOutcome<ProposalPictureChangedResponse> Run(string fwDataPath, string productVersion,
        string proposalId, Step step) =>
        ProjectStoreCommand.Run(fwDataPath, productVersion, (database, _) =>
        {
            if (!CanonicalId.TryParse(proposalId, out var id))
                return CommandOutcome<ProposalPictureChangedResponse>.Refused(ProposalTexts.Refuse("proposal.not-found",
                    FailureReason.NotFound, $"'{proposalId}' is not a full Proposal id. Use the proposalId a Proposal " +
                    "listing returns; prefixes and names are not accepted.", proposalId));
            return step(new ProposalPictureRepository(database), id);
        });

    private static CommandOutcome<ProposalPictureChangedResponse> Refuse(CanonicalId id, string message) =>
        CommandOutcome<ProposalPictureChangedResponse>.Refused(ProposalTexts.Refuse(
            RefusalCodes.ProposalPictureInvalid, FailureReason.InvalidArgument, message, id.Value));

    private static CommandOutcome<ProposalPictureChangedResponse> Result(CanonicalId id, PictureWrite write, string change,
        ProposalPictureRepository pictures) => write.Outcome switch
    {
        PictureWriteOutcome.Written => CommandOutcome<ProposalPictureChangedResponse>.Success(
            new ProposalPictureChangedResponse(id.Value, write.PictureId, change,
                change == "added" ? pictures.List(id).Where(p => p.PictureId == write.PictureId).Select(View).Single() : null)),
        PictureWriteOutcome.Closed => CommandOutcome<ProposalPictureChangedResponse>.Refused(ProposalTexts.Refuse(
            "proposal.closed", FailureReason.Refused,
            $"Proposal '{id.Value}' is decided, so its pictures can no longer change.", id.Value)),
        PictureWriteOutcome.TargetNotFound => CommandOutcome<ProposalPictureChangedResponse>.Refused(ProposalTexts.Refuse(
            "proposal.not-found", FailureReason.NotFound,
            $"Proposal '{id.Value}' has no such Comment or picture.", id.Value)),
        PictureWriteOutcome.ProposalNotFound => CommandOutcome<ProposalPictureChangedResponse>.Refused(ProposalTexts.Refuse(
            "proposal.not-found", FailureReason.NotFound, $"Proposal '{id.Value}' was not found.", id.Value)),
        _ => throw new InvalidOperationException($"Unhandled picture outcome {write.Outcome}."),
    };
}
