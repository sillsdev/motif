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
        var offset = request.CursorOffset();
        if (offset < 0)
            return CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.PagingInvalid,
                FailureReason.InvalidArgument, "The cursor must be the nextCursor a previous page returned."));
        var limit = request.Limit ?? TrialExportPage.Size;
        if (!ResultPaging.IsValid(offset, limit))
            return CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.PagingInvalid,
                FailureReason.InvalidArgument, $"Use a limit from 1 to {ResultPaging.MaxLimit}."));
        var absent = CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.TrialExportUnavailable,
            FailureReason.Refused, "No retained evidence export matches this Trial and the requested revision or Baseline."));
        return ProjectStoreCommand.ReadExisting(request.ProjectPath, absent, (database, _) =>
        {
            try
            {
                var receipt = TrialExportArtifact.Read(database, request.JobId, request.At,
                    request.Cursor is null && request.Limit is null ? null : offset, limit);
                return receipt is null ? absent : CommandOutcome<TrialExportReceipt>.Success(
                    receipt with { PerWordParserWorkLimit = FrozenWorkLimit(database, request.JobId) });
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
            {
                return CommandOutcome<TrialExportReceipt>.Refused(new Refusal(RefusalCodes.TrialExportUnavailable,
                    FailureReason.Refused, "The retained Trial evidence export could not be read."));
            }
        });
    }

    internal static SIL.Motif.Contract.Assess.ParserWorkLimit? FrozenWorkLimit(SIL.Motif.Host.Store.MotifDatabase database, string jobId)
    {
        var job = new JobRepository(database).Get(jobId);
        return job is null ? null : TrialJobInput.Parse(job.InputJson).Limits?.PerWordParserWorkLimit;
    }
}
