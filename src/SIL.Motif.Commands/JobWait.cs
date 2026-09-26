using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands;

/// <summary>Waits asynchronously for one durable job and requests cancellation when the wait is cancelled.</summary>
public static class JobWait
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Polls a job until it finishes, the timeout elapses, or the caller cancels.</summary>
    /// <param name="projectPath">The FieldWorks project that owns the job.</param>
    /// <param name="jobId">The durable job identity to wait for.</param>
    /// <param name="progress">Receives each status observed while waiting.</param>
    /// <param name="cancellationToken">Cancels the job when the caller stops waiting.</param>
    /// <param name="timeout">The maximum wait, or <see langword="null"/> for no deadline.</param>
    /// <param name="productVersion">The version used to validate the project's store.</param>
    /// <param name="cancelOnTimeout">Requests cancellation if the wait deadline expires.</param>
    public static async Task<CommandOutcome<JobStatusResponse>> WaitAsync(
        string projectPath,
        string jobId,
        IProgress<JobStatusResponse>? progress,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        string? productVersion = null,
        bool cancelOnTimeout = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var version = productVersion ?? MotifProductVersion.CurrentText;
        DateTimeOffset? deadline = timeout is { } limit
            ? DateTimeOffset.UtcNow + (limit > TimeSpan.Zero ? limit : TimeSpan.Zero)
            : null;
        while (true)
        {
            var status = await Task.Run(() => JobCommands.Show(
                new ShowJobRequest(projectPath, jobId, version))).ConfigureAwait(false);
            if (!status.Succeeded) return CommandOutcome<JobStatusResponse>.Refused(status.Refusal!);

            var value = status.Value!;
            progress?.Report(value);
            if (value.Status is { } state && JobStateMachine.IsTerminal(state))
                return CommandOutcome<JobStatusResponse>.Success(value);
            if (cancellationToken.IsCancellationRequested)
                return await CancelAndRefuseAsync(projectPath, jobId, version, progress).ConfigureAwait(false);
            if (deadline is { } due && DateTimeOffset.UtcNow >= due)
            {
                return await WaitTimeoutAsync(projectPath, jobId, version, value, timeout!.Value,
                    cancelOnTimeout, progress).ConfigureAwait(false);
            }

            var delay = deadline is { } next ? next - DateTimeOffset.UtcNow : PollInterval;
            if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
            try
            {
                await Task.Delay(delay < PollInterval ? delay : PollInterval, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return await CancelAndRefuseAsync(projectPath, jobId, version, progress).ConfigureAwait(false);
            }
        }
    }

    private static async Task<CommandOutcome<JobStatusResponse>> CancelAndRefuseAsync(
        string projectPath, string jobId, string productVersion, IProgress<JobStatusResponse>? progress)
    {
        var cancelled = await Task.Run(() => JobCommands.Cancel(
            new CancelJobRequest(projectPath, jobId, productVersion))).ConfigureAwait(false);
        if (cancelled.Succeeded) progress?.Report(cancelled.Value!);
        if (await FinishedInsteadAsync(cancelled, projectPath, jobId, productVersion, progress)
            .ConfigureAwait(false) is { } finished)
            return CommandOutcome<JobStatusResponse>.Success(finished);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = jobId,
            ["jobCancelled"] = cancelled.Succeeded ? "true" : "false",
        };
        if (cancelled.Succeeded && cancelled.Value!.Status is { } status)
            facts["jobStatus"] = JobStatusJson.ToWire(status);
        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
            "job.wait-cancelled", FailureReason.Cancelled,
            cancelled.Succeeded
                ? "Waiting for job '" + jobId + "' was cancelled."
                : "Waiting for job '" + jobId + "' was cancelled, but Motif could not request job cancellation.",
            facts));
    }

    // A cancel refused because the job already finished means the finished result wins, on either path.
    private static async Task<JobStatusResponse?> FinishedInsteadAsync(CommandOutcome<JobStatusResponse> cancelled,
        string projectPath, string jobId, string productVersion, IProgress<JobStatusResponse>? progress)
    {
        if (cancelled.Succeeded || cancelled.Refusal?.Code != "job.already-finished") return null;
        var latest = await Task.Run(() => JobCommands.Show(
            new ShowJobRequest(projectPath, jobId, productVersion))).ConfigureAwait(false);
        if (!latest.Succeeded || latest.Value!.Status is not { } status || !JobStateMachine.IsTerminal(status))
            return null;
        progress?.Report(latest.Value);
        return latest.Value;
    }

    private static async Task<CommandOutcome<JobStatusResponse>> WaitTimeoutAsync(string projectPath,
        string jobId, string productVersion, JobStatusResponse status, TimeSpan timeout, bool cancelOnTimeout,
        IProgress<JobStatusResponse>? progress)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = jobId,
            ["status"] = JobStatusJson.ToWire(status.Status.Value),
        };
        var message = "Timed out after " + timeout + " waiting for job '" + jobId + "' to finish; ";
        var checkAgain = "Check again with 'jobs show " + jobId + " --project <fwdata>'.";
        if (!cancelOnTimeout)
        {
            message += "it was " + JobStatusJson.ToWire(status.Status.Value) + ". " + checkAgain;
        }
        else
        {
            var cancellation = await Task.Run(() => JobCommands.Cancel(
                new CancelJobRequest(projectPath, jobId, productVersion))).ConfigureAwait(false);
            if (await FinishedInsteadAsync(cancellation, projectPath, jobId, productVersion, progress)
                .ConfigureAwait(false) is { } finished)
                return CommandOutcome<JobStatusResponse>.Success(finished);
            facts["jobCancelled"] = cancellation.Succeeded ? "true" : "false";
            var cancelledStatus = cancellation.Succeeded ? cancellation.Value!.Status : null;
            if (cancelledStatus is { } known) facts["jobStatus"] = JobStatusJson.ToWire(known);
            message += !cancellation.Succeeded
                ? "cancellation was refused, so the job keeps running. " + checkAgain
                : cancelledStatus == JobStatus.Cancelled
                    ? "Motif cancelled the job."
                    : "Motif requested cancellation of the job.";
        }
        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
            "job.wait-timeout", FailureReason.Busy, message, facts));
    }
}
