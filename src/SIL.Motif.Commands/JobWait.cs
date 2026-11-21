using System.Diagnostics;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;

namespace SIL.Motif.Commands;

/// <summary>Waits asynchronously for one durable job and requests cancellation when the wait is cancelled.</summary>
public static class JobWait
{
    /// <summary>Test-only: milliseconds a terminal wait allows for cleanup, in place of eight seconds.</summary>
    public const string CleanupBudgetVariable = "MOTIF_TEST_TERMINAL_CLEANUP_MS";

    private static readonly TimeSpan TerminalCleanupBudget =
        int.TryParse(AmbientEnvironment.Get(CleanupBudgetVariable), out var milliseconds) && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds) : TimeSpan.FromSeconds(8);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>How long a job may sit Queued before a wait wakes the runner again.</summary>
    public static readonly TimeSpan DefaultRewakeAfter = TimeSpan.FromSeconds(5);

    /// <summary>How long a job may sit Queued, unclaimed, before a wait says so rather than waiting on in silence.</summary>
    public static readonly TimeSpan DefaultStallNoticeAfter = TimeSpan.FromSeconds(60);

    /// <summary>Polls a job until it finishes, the timeout elapses, or the caller cancels.</summary>
    /// <param name="projectPath">The FieldWorks project that owns the job.</param>
    /// <param name="jobId">The durable job identity to wait for.</param>
    /// <param name="progress">Receives each status observed while waiting.</param>
    /// <param name="cancellationToken">Cancels the job when the caller stops waiting.</param>
    /// <param name="timeout">The maximum wait, or <see langword="null"/> for no deadline.</param>
    /// <param name="productVersion">The version used to validate the project's store.</param>
    /// <param name="cancelOnTimeout">Requests cancellation if the wait deadline expires.</param>
    /// <param name="wakeRunner">
    /// Starts a runner again while the job is still Queued, which a runner that exited, never started or lost
    /// its ownership leaves unclaimed. Called at most once per <paramref name="rewakeAfter"/>.
    /// </param>
    /// <param name="rewakeAfter">The queued time before the first wake and between wakes; five seconds when unset.</param>
    /// <param name="reportStall">Told once when the job stays Queued, unclaimed, past <paramref name="stallNoticeAfter"/>.</param>
    /// <param name="stallNoticeAfter">The unclaimed time before the notice; sixty seconds when unset.</param>
    /// <param name="waitForTerminal">On timeout or caller cancellation, cancel and allow up to eight seconds for cleanup.</param>
    /// <param name="expectedKind">The job kind required by a typed wait.</param>
    /// <param name="expectedProposal">The Proposal that must own the supplied job.</param>
    /// <param name="wakeImmediately">Wake once as soon as the queued job's identity is validated.</param>
    public static async Task<CommandOutcome<JobStatusResponse>> WaitAsync(
        string projectPath,
        string jobId,
        IProgress<JobStatusResponse>? progress,
        CancellationToken cancellationToken,
        TimeSpan? timeout,
        string? productVersion = null,
        bool cancelOnTimeout = false,
        Action? wakeRunner = null,
        TimeSpan? rewakeAfter = null,
        Action<string>? reportStall = null,
        TimeSpan? stallNoticeAfter = null,
        bool waitForTerminal = false,
        string? expectedKind = null,
        string? expectedProposal = null,
        bool wakeImmediately = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        var version = productVersion ?? MotifProductVersion.CurrentText;
        var request = new ShowJobRequest(projectPath, jobId, version);
        CommandOutcome<JobStatusResponse> Read() =>
            JobCommands.ShowForWait(request, expectedKind, expectedProposal, waitForTerminal);
        if (waitForTerminal)
            return await WaitTerminalAsync(request, Read, progress, cancellationToken,
                timeout ?? JobCommands.DefaultWaitTimeout, wakeRunner, rewakeAfter ?? DefaultRewakeAfter,
                wakeImmediately, reportStall, stallNoticeAfter ?? DefaultStallNoticeAfter).ConfigureAwait(false);

        var rewake = rewakeAfter ?? DefaultRewakeAfter;
        var lastWake = DateTimeOffset.UtcNow;
        var stallAfter = stallNoticeAfter ?? DefaultStallNoticeAfter;
        var waitStarted = DateTimeOffset.UtcNow;
        var stallReported = false;
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            var status = await Task.Run(Read).ConfigureAwait(false);
            if (!status.Succeeded) return status;

            var value = status.Value!;
            progress?.Report(value);
            if (value.Status is { } state && JobStateMachine.IsTerminal(state)) return status;
            if (cancellationToken.IsCancellationRequested)
                return await CancelAndRefuseAsync(projectPath, jobId, version, progress).ConfigureAwait(false);
            if (wakeRunner is not null && ShouldWake(value, wakeImmediately, DateTimeOffset.UtcNow - lastWake, rewake))
            {
                wakeImmediately = false;
                lastWake = DateTimeOffset.UtcNow;
                wakeRunner();
            }
            if (!stallReported && reportStall is not null && value.Status == JobStatus.Queued && !value.ClaimLost &&
                DateTimeOffset.UtcNow - waitStarted >= stallAfter)
            {
                stallReported = true;
                reportStall(StallMessage(jobId, stallAfter, timeout, version));
            }
            if (timeout is { } limit && elapsed.Elapsed >= limit)
            {
                return await WaitTimeoutAsync(projectPath, jobId, version, value, limit, cancelOnTimeout, progress)
                    .ConfigureAwait(false);
            }

            var delay = timeout is { } deadline ? deadline - elapsed.Elapsed : PollInterval;
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

    private static async Task<CommandOutcome<JobStatusResponse>> WaitTerminalAsync(
        ShowJobRequest request, Func<CommandOutcome<JobStatusResponse>> read, IProgress<JobStatusResponse>? progress,
        CancellationToken caller, TimeSpan timeout, Action? wakeRunner, TimeSpan rewake, bool wakeImmediately,
        Action<string>? reportStall, TimeSpan stallAfter)
    {
        var started = Stopwatch.StartNew();
        var startedTimestamp = Stopwatch.GetTimestamp();
        var overall = timeout + TerminalCleanupBudget;
        long EndAt(TimeSpan elapsed) => startedTimestamp + (long)(elapsed.TotalSeconds * Stopwatch.Frequency);
        var cutoff = EndAt(overall);
        using var deadline = new CancellationTokenSource(overall);
        using var callerStop = caller.Register(() =>
        {
            var remaining = overall - started.Elapsed;
            if (remaining <= TimeSpan.Zero) deadline.Cancel();
            else if (remaining > TerminalCleanupBudget)
            {
                Volatile.Write(ref cutoff, EndAt(started.Elapsed + TerminalCleanupBudget));
                deadline.CancelAfter(TerminalCleanupBudget);
            }
        });
        using var normalWait = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token);
        normalWait.CancelAfter(timeout);

        var lastWake = TimeSpan.Zero;
        var cancellationRecorded = false;
        var validated = false;
        JobStatusResponse? last = null;
        try
        {
            while (true)
            {
                var stopping = caller.IsCancellationRequested || started.Elapsed >= timeout;
                var phase = stopping ? deadline.Token : normalWait.Token;
                try
                {
                    if (!validated || last?.Status is not { } state || !JobStateMachine.IsTerminal(state))
                    {
                        var observed = await RunStoreAsync(read, phase).ConfigureAwait(false);
                        if (!observed.Succeeded)
                        {
                            if (observed.Refusal?.Reason != FailureReason.Busy) return observed;
                            await Task.Delay(PollInterval, phase).ConfigureAwait(false);
                            continue;
                        }
                        validated = true;
                        last = observed.Value!;
                        progress?.Report(last);
                    }

                    var observedJob = last!;
                    if (observedJob.Status is { } status && JobStateMachine.IsTerminal(status))
                    {
                        var released = await RunStoreAsync(() => ExecutionReleased(request, observedJob), phase)
                            .ConfigureAwait(false);
                        if (!released.Succeeded && released.Refusal?.Reason != FailureReason.Busy)
                            return CommandOutcome<JobStatusResponse>.Refused(released.Refusal!);
                        if (released.Value is { } release)
                        {
                            var cleanupComplete = release.CleanupComplete;
                            var callerCancelled = caller.IsCancellationRequested;
                            var waitEnded = callerCancelled || started.Elapsed >= timeout;
                            if (waitEnded || !cleanupComplete)
                                return TerminalRefusal(request.JobId, timeout, callerCancelled, observedJob,
                                    cancellationRecorded, cleanupComplete, unconfirmed: !waitEnded);
                            return CommandOutcome<JobStatusResponse>.Success(observedJob);
                        }
                    }
                    else if (stopping)
                    {
                        if (!cancellationRecorded || observedJob.ClaimLost)
                        {
                            var cancelled = await RunStoreAsync(() => CancelJob(request.FwDataPath, request.JobId,
                                request.ProductVersion, phase, Volatile.Read(ref cutoff)), phase).ConfigureAwait(false);
                            if (cancelled.Succeeded)
                            {
                                cancellationRecorded = true;
                                last = cancelled.Value!;
                                progress?.Report(last);
                                if (last.Status is { } ending && JobStateMachine.IsTerminal(ending)) continue;
                            }
                            else if (cancelled.Refusal is not { Reason: FailureReason.Busy } and
                                not { Code: RefusalCodes.JobAlreadyFinished }) return cancelled;
                        }
                    }
                    else if (wakeRunner is not null && ShouldWake(observedJob, wakeImmediately, started.Elapsed - lastWake, rewake))
                    {
                        wakeImmediately = false;
                        lastWake = started.Elapsed;
                        await Task.Run(wakeRunner, phase).WaitAsync(phase).ConfigureAwait(false);
                    }

                    if (!stopping && reportStall is { } notice && observedJob.Status == JobStatus.Queued &&
                        !observedJob.ClaimLost && started.Elapsed >= stallAfter)
                    {
                        reportStall = null;
                        await Task.Run(() => notice(StallMessage(request.JobId, stallAfter, timeout, request.ProductVersion)), phase)
                            .WaitAsync(phase).ConfigureAwait(false);
                    }

                    await Task.Delay(PollInterval, phase).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stopping && normalWait.IsCancellationRequested &&
                    !deadline.IsCancellationRequested)
                {
                    // Caller cancellation and the wait limit switch to cleanup under the same outer deadline.
                }
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return TerminalRefusal(request.JobId, timeout, caller.IsCancellationRequested, last,
                cancellationRecorded, cleanupComplete: false);
        }
    }

    private static bool ShouldWake(JobStatusResponse job, bool immediately, TimeSpan sinceWake, TimeSpan rewake) =>
        (job.Status == JobStatus.Queued || job.ClaimLost) && (immediately || sinceWake >= rewake) ||
        immediately && job.Status is JobStatus.WaitingForBaseline or JobStatus.WaitingForProjectHost;

    private static string StallMessage(string jobId, TimeSpan stallAfter, TimeSpan? timeout, string productVersion) =>
        "warning: job '" + jobId + "' has been queued for " + (int)stallAfter.TotalSeconds +
        " seconds and no runner has claimed it. The background runner may not have started, or may not " +
        "know this project (a project tag collision stops it being recorded; see 'motif project tag')." +
        (QueueBlocker.Find(jobId, productVersion)?.Describe() ?? "") +
        " Still waiting" + (timeout is { } shown ? " for up to " + shown : "") + ".";

    private sealed record ExecutionRelease(bool CleanupComplete);

    private static CommandOutcome<ExecutionRelease?> ExecutionReleased(ShowJobRequest request, JobStatusResponse status) =>
        ProjectStoreCommand.ReadExisting(request.FwDataPath, CommandOutcome<ExecutionRelease?>.Success(null), (database, _) =>
        {
            using var execution = JobClaims.ExecutionLock(database.FullPath, status.JobId);
            if (!execution.TryAcquire()) return CommandOutcome<ExecutionRelease?>.Success(null);
            var job = new JobRepository(database).Get(status.JobId);
            return CommandOutcome<ExecutionRelease?>.Success(job is null ? null :
                new ExecutionRelease(!JobCleanupProgress.Unconfirmed(job.ProgressJson)));
        });

    private static CommandOutcome<JobStatusResponse> TerminalRefusal(string jobId, TimeSpan timeout, bool callerCancelled,
        JobStatusResponse? last, bool cancellationRecorded, bool cleanupComplete, bool unconfirmed = false)
    {
        var status = last?.Status is { } known ? JobStatusJson.ToWire(known) : "unknown";
        var terminal = last?.Status is { } observed && JobStateMachine.IsTerminal(observed);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = jobId,
            ["status"] = status,
            ["terminal"] = terminal ? "true" : "false",
            ["cleanup"] = cleanupComplete ? "complete" : "incomplete",
            ["jobCancelled"] = cancellationRecorded ? "true" : "false",
        };
        if (last is not null) AddObservedStage(last, facts);
        if (unconfirmed)
            return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                RefusalCodes.JobWaitCleanupUnconfirmed, FailureReason.Busy,
                "The job finished, but its execution cleanup could not be confirmed.", facts));
        var message = (callerCancelled ? "Waiting for job '" : "Timed out after " + timeout + " waiting for job '") +
            jobId + (callerCancelled ? "' was cancelled." : "'.") + " The last observed status was " + status + "." +
            (cleanupComplete ? " Execution cleanup was confirmed." : " Execution cleanup could not be confirmed by the deadline.");
        if (callerCancelled)
            return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                RefusalCodes.JobWaitCancelled, FailureReason.Cancelled, message, facts));
        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
            RefusalCodes.JobWaitTimeout, FailureReason.Busy, message, facts));
    }

    private static async Task<CommandOutcome<T>> RunStoreAsync<T>(
        Func<CommandOutcome<T>> operation, CancellationToken deadline) where T : class?
    {
        try { return await Task.Run(operation, deadline).WaitAsync(deadline).ConfigureAwait(false); }
        catch (Exception exception) when (SqliteConnections.IsBusy(exception))
        {
            return CommandOutcome<T>.Refused(new Refusal(
                RefusalCodes.ProjectBusy, FailureReason.Busy, "The project store is busy."));
        }
    }

    private static CommandOutcome<JobStatusResponse> CancelJob(string projectPath, string jobId, string productVersion,
        CancellationToken deadline = default, long? terminalCleanupDeadline = null) =>
        // A store that is not current holds no job to cancel.
        ProjectStoreCommand.RefuseUnlessStoreIsCurrent(projectPath, _ => CommandOutcome<JobStatusResponse>.Refused(
            JobCommands.JobNotFound(jobId)))
        ?? JobCommands.CancelForWait(new CancelJobRequest(projectPath, jobId, productVersion), deadline, terminalCleanupDeadline);

    private static Task<CommandOutcome<JobStatusResponse>> CancelJobAsync(string projectPath, string jobId,
        string productVersion) => Task.Run(() => CancelJob(projectPath, jobId, productVersion));

    private static async Task<CommandOutcome<JobStatusResponse>> CancelAndRefuseAsync(
        string projectPath, string jobId, string productVersion, IProgress<JobStatusResponse>? progress)
    {
        var cancelled = await CancelJobAsync(projectPath, jobId, productVersion).ConfigureAwait(false);
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
        var statusText = status.Status is { } currentStatus
            ? JobStatusJson.ToWire(currentStatus)
            : "unknown";
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["jobId"] = jobId,
            ["status"] = statusText,
        };
        var message = "Timed out after " + timeout + " waiting for job '" + jobId + "' to finish; ";
        var checkAgain = "Check again with 'jobs show " + jobId + " --project <fwdata>'.";
        var resume = "This is a wait timeout, not a failed job: it keeps running. Resume the wait with " +
            "'jobs wait " + jobId + " --project <fwdata> --wait-timeout-ms <ms>'.";
        AddObservedStage(status, facts);
        var unclaimed = false;
        if (status.ClaimLost) facts["claimLost"] = "true";
        if (!cancelOnTimeout)
        {
            // A runner may claim the job between the last poll and now, so the verdict reads the store again.
            var latest = status.Status == JobStatus.Queued
                ? await Task.Run(() => JobCommands.Show(new ShowJobRequest(projectPath, jobId, productVersion)))
                    .ConfigureAwait(false)
                : null;
            if (latest is { Succeeded: true, Value.Status: { } fresh })
            {
                status = latest.Value!;
                statusText = JobStatusJson.ToWire(fresh);
            }
            unclaimed = status.Status == JobStatus.Queued;
            message += "it was " + statusText + ". " + checkAgain + " " + resume;
        }
        else
        {
            var cancellation = await CancelJobAsync(projectPath, jobId, productVersion).ConfigureAwait(false);
            if (await FinishedInsteadAsync(cancellation, projectPath, jobId, productVersion, progress)
                .ConfigureAwait(false) is { } finished)
                return CommandOutcome<JobStatusResponse>.Success(finished);
            facts["jobCancelled"] = cancellation.Succeeded ? "true" : "false";
            var cancelledStatus = cancellation.Succeeded ? cancellation.Value!.Status : null;
            if (cancelledStatus is { } known) facts["jobStatus"] = JobStatusJson.ToWire(known);
            // Only a Queued job cancels straight to Cancelled; a job a runner just claimed stays Running.
            unclaimed = status.Status == JobStatus.Queued && cancelledStatus == JobStatus.Cancelled;
            message += !cancellation.Succeeded
                ? "cancellation was refused, so the job keeps running. " + checkAgain + " " + resume
                : cancelledStatus == JobStatus.Cancelled
                    ? "Motif cancelled the job."
                    : "Motif requested cancellation of the job.";
        }
        QueueBlocker? blocker = null;
        if (unclaimed)
        {
            facts["unclaimed"] = "true";
            blocker = await Task.Run(() => QueueBlocker.Find(jobId, productVersion)).ConfigureAwait(false);
            blocker?.AddFacts(facts);
        }
        if (status.ClaimLost)
            message += " The runner that claimed it stopped, so nothing is working on it. Run any Motif command that " +
                "queues work, or start `SIL.Motif.Worker`, to restart one; it takes the job back.";
        if (unclaimed)
            message += " No runner has claimed it: the background runner may not have started, may have exited, " +
                "or an existing runner may not be sweeping this project. Start `SIL.Motif.Worker`, or run a " +
                "command that queues work, to wake one." + (blocker?.Describe() ?? "");
        if (status.StageProgress is { } stage)
            message += " The last observed stage was '" + stage.Stage + "'" +
                (string.IsNullOrWhiteSpace(stage.Detail) ? "." : " (" + stage.Detail + ").");
        if (!cancelOnTimeout && !status.ClaimLost && status.StageProgress?.Stage == "waiting-for-parser-slot")
            message += " It is queued behind other parses on this computer and stays queued; wait again with " +
                "'jobs wait " + jobId + " --project <fwdata>', or give a longer --wait-timeout-ms.";
        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
            "job.wait-timeout", FailureReason.Busy, message, facts));
    }

    private static void AddObservedStage(JobStatusResponse status, IDictionary<string, string> facts)
    {
        if (status.StageProgress is not { } stage) return;
        facts["stage"] = stage.Stage;
        if (stage.Detail is not null) facts["stageDetail"] = stage.Detail;
    }
}
