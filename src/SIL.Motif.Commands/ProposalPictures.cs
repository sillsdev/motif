using System.Globalization;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The pictures on a Proposal. Only a person adds or removes one: every picture, from the window or the CLI, is
/// scaled by <see cref="PictureScaler"/> to a metadata-free WebP of at most <see cref="MaxBytes"/> and
/// <see cref="MaxSide"/> pixels before it is stored. Pictures live in the project store, count in its budget and are never pruned.
/// </summary>
public static class ProposalPictures
{
    /// <summary>The most bytes one stored picture may take: 1 MiB.</summary>
    public const int MaxBytes = 1024 * 1024;

    /// <summary>The longest side, in pixels, a stored picture may have; a larger picture is scaled down to it on attach.</summary>
    public const int MaxSide = 1000;

    /// <summary>How many pictures one Proposal holds; a Proposal over it keeps what it has and takes no more.</summary>
    public const int MaxPictures = ProposalPictureRepository.MaxPicturesPerProposal;

    /// <summary>The most characters a picture's caption may have.</summary>
    public const int MaxCaptionLength = 200;

    /// <summary>Scales a picture and adds it to an open Proposal, or to one of its Comments.</summary>
    public static CommandOutcome<ProposalPictureChangedResponse> Add(AddPictureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Run(request.FwDataPath, request.ProductVersion, request.ProposalId, (pictures, id) =>
        {
            if (request.Bytes is not { Length: > 0 })
                return Refuse(id, "A picture cannot be empty.");
            var scaled = PictureScaler.Scale(request.Bytes);
            switch (scaled.Status)
            {
                case PictureScaleStatus.Unsupported:
                    return Refuse(id, "Only WebP, PNG, JPEG, GIF and BMP pictures can be added.");
                case PictureScaleStatus.Unreadable:
                    return Refuse(id, "This picture cannot be opened; it may be damaged. Check the file and try again.");
                case PictureScaleStatus.TooLarge:
                    return Refuse(id, string.Format(CultureInfo.InvariantCulture,
                        "This picture is too large to attach: Motif reads files up to {0} MiB and about {1} megapixels, " +
                        "and scales them down to {2} bytes. Reduce it and try again.",
                        PictureScaler.MaxSourceBytes / (1024 * 1024), PictureScaler.MaxSourcePixels / 1_000_000, MaxBytes));
            }
            var stored = scaled.Bytes!;
            if (stored.Length > MaxBytes || PictureSniffer.Read(stored) is not var (mediaType, width, height))
                return Refuse(id, "This picture cannot be stored; try another.");
            var caption = request.Caption?.Trim();
            if (caption?.Contains('\n') == true || caption?.Contains('\r') == true)
                return Refuse(id, "A caption is one line; remove the line breaks and try again.");
            if (caption is { Length: > MaxCaptionLength })
                return Refuse(id, string.Format(CultureInfo.InvariantCulture,
                    "This caption is {0} characters; a caption is at most {1}. Shorten it and try again.",
                    caption.Length, MaxCaptionLength));
            var write = pictures.Add(id, request.CommentId, mediaType, width, height, stored,
                string.IsNullOrEmpty(caption) ? null : caption);
            return write.Outcome switch
            {
                PictureWriteOutcome.TooMany => Refuse(id,
                    $"A Proposal holds at most {ProposalPictureRepository.MaxPicturesPerProposal} pictures. Remove one to add another."),
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
        record.MediaType, record.Width, record.Height, record.ByteCount, record.CreatedUtc, record.Caption);

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
