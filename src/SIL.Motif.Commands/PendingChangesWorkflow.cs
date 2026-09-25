using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.Commands;

/// <summary>Applies and measures pending changes through the catalog's UI-independent workflow.</summary>
public static class PendingChangesWorkflow
{
    /// <summary>Finalizes, checks, evaluates, and applies the named pending revision as one workflow.</summary>
    /// <param name="request">The project, pending Draft, expected revision, and applying user.</param>
    /// <param name="cancellationToken">Cancels the queued Dry Run and reopens the Draft while waiting.</param>
    public static CommandOutcome<ApplyProjection> Apply(
        ApplyPendingRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = MotifProductVersion.CurrentText;
        var pending = PendingChanges.Load(new PendingChangesRequest(request.ProjectPath, version));
        if (!pending.Succeeded) return CommandOutcome<ApplyProjection>.Refused(pending.Refusal!);
        if (pending.Value!.DraftId != request.DraftId || pending.Value.Revision != request.Revision)
            return RefuseApply("review.changes-changed",
                "The changes have changed. Check the numbers again before applying.");
        if (pending.Value.Changes.Count == 0 || pending.Value.FitSummary.Count != pending.Value.Changes.Count ||
            pending.Value.FitSummary.Any(fit => !fit.StillFits))
            return RefuseApply("review.change-no-longer-fits",
                "One or more changes no longer fit the project. Remove those changes first.");

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            request.ProjectPath, version, PendingChanges.DraftName));
        if (!finalized.Succeeded) return CommandOutcome<ApplyProjection>.Refused(finalized.Refusal!);
        var proposalId = finalized.Value!.ProposalId;

        var fit = ProposalCommands.Preflight(new PreflightRequest(request.ProjectPath, version, proposalId));
        if (!fit.Succeeded) return ReopenAfterRefusal(request, fit.Refusal!);
        if (fit.Value!.Changes.Any(operation => !operation.StillFits))
            return ReopenAfterRefusal(request, new Refusal("review.change-no-longer-fits",
                FailureReason.Refused, "One or more changes no longer fit the project. Remove those changes first."));

        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(request.ProjectPath, version, proposalId));
        if (!queued.Succeeded) return ReopenAfterRefusal(request, queued.Refusal!);
        RunnerKick.After();
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            request.ProjectPath, version, proposalId, queued.Value!.JobId, JobCommands.DefaultWaitTimeout),
            cancellationToken);
        if (!dryRun.Succeeded) return ReopenAfterRefusal(request, dryRun.Refusal!);

        var applied = ProposalCommands.Apply(new ApplyRequest(
            request.ProjectPath, version, proposalId, request.User));
        if (applied.Succeeded || applied.Refusal?.Code == "apply.reconciliation-needed") return applied;
        return ReopenAfterRefusal(request, applied.Refusal!);
    }

    /// <summary>Runs a Trial for the selected words and returns its recorded correctness evidence.</summary>
    /// <param name="request">The project, pending Draft, revision, words, and earlier Assessment identity.</param>
    /// <param name="progress">Receives the number of words completed and the current word.</param>
    /// <param name="cancellationToken">Cancels the Trial job and stops waiting for it.</param>
    public static async Task<CommandOutcome<MeasurePendingResult>> Measure(
        MeasurePendingRequest request,
        IProgress<MeasureProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(new MeasureProgress(0, request.Words.Count, null));
        var version = MotifProductVersion.CurrentText;
        var pending = await Task.Run(() => PendingChanges.Load(
            new PendingChangesRequest(request.ProjectPath, version))).ConfigureAwait(false);
        if (!pending.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(pending.Refusal!);
        if (pending.Value!.DraftId != request.DraftId || pending.Value.Revision != request.Revision)
            return RefuseMeasure("The changes have changed. Check the numbers again before measuring.");
        if (cancellationToken.IsCancellationRequested) return CancelledMeasure();

        var queued = await Task.Run(() => JobCommands.EnqueueTrial(new EnqueueTrialRequest(
            request.ProjectPath, version, request.DraftId, Words: request.Words))).ConfigureAwait(false);
        if (!queued.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(queued.Refusal!);
        var jobId = queued.Value!.JobId;
        RunnerKick.After();

        MeasureProgress? lastProgress = null;
        var jobProgress = new JobStatusProgress(status =>
        {
            if (status.TrialProgress is not { } wordProgress) return;
            var next = new MeasureProgress(wordProgress.Completed, wordProgress.Total, wordProgress.CurrentWord);
            if (next == lastProgress) return;
            progress.Report(next);
            lastProgress = next;
        });
        var waited = await JobWait.WaitAsync(
            request.ProjectPath, jobId, jobProgress, cancellationToken, JobCommands.DefaultWaitTimeout, version)
            .ConfigureAwait(false);
        if (!waited.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(waited.Refusal!);
        if (waited.Value!.Status != JobStatus.Completed)
            return RefuseMeasure("The check of these words did not complete. Try it again.");

        var recorded = await Task.Run(() => JobCommands.Assessments(
            new JobAssessmentsRequest(request.ProjectPath, jobId, version))).ConfigureAwait(false);
        if (!recorded.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(recorded.Refusal!);
        var correctness = recorded.Value!.Assessments.LastOrDefault(item => item.Kind == "Correctness");
        if (correctness is null) return RefuseMeasure("The check did not measure approved analyses.");
        var numbers = await Task.Run(() => ReviewNumbersCommand.Read(new ReviewNumbersCommand.Request(
            request.ProjectPath, request.BeforeCorrectnessAssessmentId, correctness.AssessmentId,
            request.Words.Count))).ConfigureAwait(false);
        if (!numbers.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(numbers.Refusal!);
        progress.Report(new MeasureProgress(request.Words.Count, request.Words.Count, null));
        return CommandOutcome<MeasurePendingResult>.Success(new MeasurePendingResult(
            jobId, request.Revision, numbers.Value!.Text, numbers.Value.EvidenceComplete));
    }

    private static CommandOutcome<ApplyProjection> ReopenAfterRefusal(
        ApplyPendingRequest request, Refusal reason)
    {
        var reopened = ProposalCommands.Reopen(new ReopenRequest(
            request.ProjectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName, request.DraftId));
        return reopened.Succeeded ? CommandOutcome<ApplyProjection>.Refused(reason)
            : RefuseApply("review.reopen-failed",
                reason.Message + " The changes could not be reopened: " + reopened.Refusal!.Message);
    }

    private static CommandOutcome<ApplyProjection> RefuseApply(string code, string message) =>
        CommandOutcome<ApplyProjection>.Refused(new Refusal(code, FailureReason.Refused, message));

    private static CommandOutcome<MeasurePendingResult> RefuseMeasure(string message) =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            "review.measurement-incomplete", FailureReason.Refused, message));

    private static CommandOutcome<MeasurePendingResult> CancelledMeasure() =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            "job.wait-cancelled", FailureReason.Cancelled, "The check was cancelled."));

    private sealed class JobStatusProgress(Action<JobStatusResponse> report) : IProgress<JobStatusResponse>
    {
        public void Report(JobStatusResponse value) => report(value);
    }
}
