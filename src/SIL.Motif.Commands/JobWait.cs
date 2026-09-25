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
    /// <param name="timeout">The maximum time to wait before returning a busy refusal.</param>
    /// <param name="productVersion">The version used to validate the project's store.</param>
    public static async Task<CommandOutcome<JobStatusResponse>> WaitAsync(
        string projectPath,
        string jobId,
        IProgress<JobStatusResponse>? progress,
        CancellationToken cancellationToken,
        TimeSpan timeout,
        string? productVersion = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var version = productVersion ?? MotifProductVersion.CurrentText;
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
                return await CancelAndRefuseAsync(projectPath, jobId, version, progress).ConfigureAwait(false);

            var status = await Task.Run(() => JobCommands.Show(
                new ShowJobRequest(projectPath, jobId, version))).ConfigureAwait(false);
            if (!status.Succeeded) return CommandOutcome<JobStatusResponse>.Refused(status.Refusal!);

            var value = status.Value!;
            progress?.Report(value);
            if (cancellationToken.IsCancellationRequested)
                return await CancelAndRefuseAsync(projectPath, jobId, version, progress).ConfigureAwait(false);
            if (value.Status is { } state && JobStateMachine.IsTerminal(state))
                return CommandOutcome<JobStatusResponse>.Success(value);
            if (DateTimeOffset.UtcNow >= deadline)
                return CommandOutcome<JobStatusResponse>.Refused(WaitTimeout(jobId, value, timeout));

            var remaining = deadline - DateTimeOffset.UtcNow;
            try
            {
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken)
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
        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
            "job.wait-cancelled", FailureReason.Cancelled,
            "Waiting for job '" + jobId + "' was cancelled.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["jobId"] = jobId }));
    }

    private static Refusal WaitTimeout(string jobId, JobStatusResponse status, TimeSpan timeout) => new(
        "job.wait-timeout", FailureReason.Busy,
        "Timed out after " + timeout + " waiting for job '" + jobId + "' to finish; it is still " +
        JobStatusJson.ToWire(status.Status!.Value) +
        ". Check again with 'jobs show " + jobId + " --project <fwdata>'.",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = jobId,
            ["status"] = JobStatusJson.ToWire(status.Status.Value),
        });
}
