using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public async Task<CommandOutcome<JobEnqueuedResponse>> EnqueueParsimonyReportAsync(
        EnqueueParsimonyReportRequest request, CancellationToken cancellationToken)
    {
        var queued = await Task.Run(() => cancellationToken.IsCancellationRequested
            ? WaitCancelled<JobEnqueuedResponse>()
            : ParsimonyCommands.Enqueue(request, _options.ParserPath)).ConfigureAwait(false);
        if (!queued.Succeeded) return queued;
        try { _options.RunnerLauncher.Start(request.FwDataPath); }
        catch (Exception exception)
        {
            await JobWait.WaitAsync(request.FwDataPath, queued.Value!.JobId, null,
                new CancellationToken(canceled: true), null, request.ProductVersion).ConfigureAwait(false);
            var drained = await JobWait.WaitAsync(request.FwDataPath, queued.Value.JobId, null,
                CancellationToken.None, null, request.ProductVersion).ConfigureAwait(false);
            return CommandOutcome<JobEnqueuedResponse>.Refused(!drained.Succeeded ? drained.Refusal! :
                new Refusal(ParsimonyRefusalCodes.RunnerStartFailed, FailureReason.Refused, exception.Message));
        }
        return queued;
    }

    public Task<CommandOutcome<ParsimonyReportSetResponse?>> ReadLatestParsimonyReportSetAsync(
        ReadLatestParsimonyReportRequest request, CancellationToken cancellationToken) =>
        ParsimonyReportSetQuery.QueryAsync(request, _options.ParserPath, cancellationToken);
}
