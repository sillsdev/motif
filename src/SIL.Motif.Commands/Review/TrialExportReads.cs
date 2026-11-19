using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands.Review;

/// <summary>Reads the bounded receipt from an artifact already published by a Trial.</summary>
public static class TrialExportReads
{
    /// <summary>Locates the artifact without relying on the original Job or its Assessment detail.</summary>
    public static CommandOutcome<TrialExportReceipt> Read(ReadTrialExportRequest request)
    {
        if (request.Page is < 1)
            return CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.EvidenceInvalidPage,
                FailureReason.InvalidArgument, "The Trial export page must be a positive integer."));
        var absent = CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.TrialExportUnavailable,
            FailureReason.Refused, "No retained evidence export matches this Trial and the requested revision or Baseline."));
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, absent, (database, _) =>
        {
            try
            {
                var receipt = TrialExportArtifact.Read(database, request.JobId, request.At, request.Page);
                return receipt is null ? absent : CommandOutcome<TrialExportReceipt>.Success(receipt);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                return CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.TrialExportUnavailable,
                    FailureReason.Refused, "The retained Trial evidence export could not be read."));
            }
        });
    }
}
