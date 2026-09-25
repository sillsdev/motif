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
    /// <summary>Finalizes, checks, evaluates, and applies the pending changes as one workflow.</summary>
    /// <param name="request">The project, pending Draft, expected revision, and applying user.</param>
    /// <param name="cancellationToken">Cancels the queued Dry Run and reopens the Draft while waiting.</param>
    /// <param name="dryRunTimeout">The wait bound, or <see langword="null"/> to use the command default.</param>
    public static CommandOutcome<ApplyProjection> Apply(
        ApplyPendingRequest request, CancellationToken cancellationToken = default,
        TimeSpan? dryRunTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = MotifProductVersion.CurrentText;
        var pending = PendingChanges.Load(new PendingChangesRequest(request.ProjectPath, version));
        if (!pending.Succeeded) return CommandOutcome<ApplyProjection>.Refused(pending.Refusal!);
        var snapshot = pending.Value!;
        if (snapshot.Changes.Count == 0 || snapshot.DraftId is not { } draftId)
            return CommandOutcome<ApplyProjection>.Refused(new Refusal(
                "apply.nothing-pending", FailureReason.NoChanges, "There are no pending changes to apply."));
        if (request.DraftId is { } expectedDraft && expectedDraft != draftId ||
            request.Revision is { } expectedRevision && expectedRevision != snapshot.Revision)
            return RefuseApply("apply.changes-changed",
                "The changes changed. Reload and check them before applying.");
        if (snapshot.FitSummary.Count != snapshot.Changes.Count ||
            snapshot.FitSummary.Any(fit => !fit.StillFits))
            return RefuseApply("apply.change-no-longer-fits",
                "One or more changes no longer fit the project. Remove those changes first.");
        var resolvedRequest = request with { DraftId = draftId, Revision = snapshot.Revision };

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            request.ProjectPath, version, PendingChanges.DraftName, snapshot.Revision));
        if (!finalized.Succeeded)
        {
            if (finalized.Refusal?.Code == "draft.revision-conflict")
                return RefuseApply("apply.changes-changed",
                    "The changes changed. Reload and check them before applying.");
            return CommandOutcome<ApplyProjection>.Refused(finalized.Refusal!);
        }
        var proposalId = finalized.Value!.ProposalId;

        var fit = ProposalCommands.Preflight(new PreflightRequest(request.ProjectPath, version, proposalId));
        if (!fit.Succeeded) return ReopenAfterRefusal(resolvedRequest, fit.Refusal!);
        if (fit.Value!.Changes.Any(operation => !operation.StillFits))
            return ReopenAfterRefusal(resolvedRequest, new Refusal("apply.change-no-longer-fits",
                FailureReason.Refused, "One or more changes no longer fit the project. Remove those changes first."));

        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(request.ProjectPath, version, proposalId));
        if (!queued.Succeeded) return ReopenAfterRefusal(resolvedRequest, queued.Refusal!);
        RunnerKick.After();
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            request.ProjectPath, version, proposalId, queued.Value!.JobId,
            dryRunTimeout ?? JobCommands.DefaultWaitTimeout), cancellationToken, cancelOnTimeout: true);
        if (!dryRun.Succeeded) return ReopenAfterRefusal(resolvedRequest, dryRun.Refusal!);

        var applied = ProposalCommands.Apply(new ApplyRequest(
            request.ProjectPath, version, proposalId, request.User));
        if (applied.Succeeded || applied.Refusal?.Code == "apply.reconciliation-needed") return applied;
        return ReopenAfterRefusal(resolvedRequest, applied.Refusal!);
    }

    /// <summary>Runs a Trial for the selected words and returns its recorded correctness evidence.</summary>
    /// <param name="request">The project, pending Draft, revision, words, and earlier Assessment identity.</param>
    /// <param name="progress">Receives the number of words completed and the current word.</param>
    /// <param name="cancellationToken">Cancels the Trial job and stops waiting for it.</param>
    /// <param name="waitTimeout">The wait bound, or <see langword="null"/> to wait until completion or cancellation.</param>
    public static async Task<CommandOutcome<MeasurePendingResult>> Measure(
        MeasurePendingRequest request,
        IProgress<MeasureProgress> progress,
        CancellationToken cancellationToken,
        TimeSpan? waitTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(new MeasureProgress(0, request.Words.Count, null));
        var version = MotifProductVersion.CurrentText;
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
            request.ProjectPath, jobId, jobProgress, cancellationToken, waitTimeout, version,
            cancelOnTimeout: true)
            .ConfigureAwait(false);
        if (!waited.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(waited.Refusal!);
        if (waited.Value!.Status != JobStatus.Completed)
            return RefuseMeasure("trial.measurement-incomplete", "The check of these words did not complete. Try it again.");

        var recorded = await Task.Run(() => JobCommands.Assessments(
            new JobAssessmentsRequest(request.ProjectPath, jobId, version))).ConfigureAwait(false);
        if (!recorded.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(recorded.Refusal!);
        var correctness = recorded.Value!.Assessments.LastOrDefault(item => item.Kind == "Correctness");
        if (correctness is null)
            return RefuseMeasure("trial.measurement-incomplete", "The check did not measure approved analyses.");
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
            request.ProjectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName, request.DraftId!));
        return reopened.Succeeded ? CommandOutcome<ApplyProjection>.Refused(reason)
            : RefuseApply("apply.reopen-failed",
                reason.Message + " The changes could not be reopened: " + reopened.Refusal!.Message);
    }

    private static CommandOutcome<ApplyProjection> RefuseApply(string code, string message) =>
        CommandOutcome<ApplyProjection>.Refused(new Refusal(code, FailureReason.Refused, message));

    private static CommandOutcome<MeasurePendingResult> RefuseMeasure(string code, string message) =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            code, FailureReason.Refused, message));

    private static CommandOutcome<MeasurePendingResult> CancelledMeasure() =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            "job.wait-cancelled", FailureReason.Cancelled, "The check was cancelled."));

    private sealed class JobStatusProgress(Action<JobStatusResponse> report) : IProgress<JobStatusResponse>
    {
        public void Report(JobStatusResponse value) => report(value);
    }
}
