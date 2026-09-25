using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<ApplyProjection>> ApplyReviewAsync(
        ReviewApplyRequest request, CancellationToken cancellationToken) => OneAtATime(() =>
    {
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
            request.ProjectPath, version, proposalId, queued.Value!.JobId, TimeSpan.FromMinutes(2)));
        if (!dryRun.Succeeded) return ReopenAfterRefusal(request, dryRun.Refusal!);
        var applied = ProposalCommands.Apply(new ApplyRequest(
            request.ProjectPath, version, proposalId, Environment.UserName));
        if (applied.Succeeded || applied.Refusal?.Code == "apply.reconciliation-needed") return applied;
        return ReopenAfterRefusal(request, applied.Refusal!);
    });

    private static CommandOutcome<ApplyProjection> ReopenAfterRefusal(ReviewApplyRequest request, Refusal reason)
    {
        var reopened = ProposalCommands.Reopen(new ReopenRequest(
            request.ProjectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName, request.DraftId));
        return reopened.Succeeded ? CommandOutcome<ApplyProjection>.Refused(reason)
            : RefuseApply("review.reopen-failed",
                reason.Message + " The changes could not be reopened: " + reopened.Refusal!.Message);
    }

    private static CommandOutcome<ApplyProjection> RefuseApply(string code, string message) =>
        CommandOutcome<ApplyProjection>.Refused(new Refusal(
            code, FailureReason.Refused, message));

    public async Task<CommandOutcome<ReviewTrialResult>> RunReviewTrialAsync(
        ReviewTrialRequest request, IProgress<ReviewTrialProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(new ReviewTrialProgress(0, request.Words.Count, null));
        var queued = await Task.Run(() => JobCommands.EnqueueTrial(new EnqueueTrialRequest(
            request.ProjectPath, MotifProductVersion.CurrentText, request.DraftId, Words: request.Words)));
        if (!queued.Succeeded) return CommandOutcome<ReviewTrialResult>.Refused(queued.Refusal!);
        var jobId = queued.Value!.JobId;
        RunnerKick.After();
        ReviewTrialProgress? lastProgress = null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var state = await Task.Run(() => JobCommands.Show(new ShowJobRequest(
                    request.ProjectPath, jobId, MotifProductVersion.CurrentText)));
                if (!state.Succeeded) return CommandOutcome<ReviewTrialResult>.Refused(state.Refusal!);
                if (state.Value!.TrialProgress is { } wordProgress)
                {
                    var next = new ReviewTrialProgress(wordProgress.Completed, wordProgress.Total,
                        wordProgress.CurrentWord);
                    if (next != lastProgress) progress.Report(next);
                    lastProgress = next;
                }
                if (state.Value!.Status == JobStatus.Completed) break;
                if (state.Value.Status is JobStatus.Failed or JobStatus.Cancelled or
                    JobStatus.CompletedWithAssessmentFailure or JobStatus.Interrupted)
                    return RefuseTrial("The check of these words did not complete. Try it again.");
                await Task.Delay(250, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            await Task.Run(() => JobCommands.Cancel(new CancelJobRequest(
                request.ProjectPath, jobId, MotifProductVersion.CurrentText)));
            return CommandOutcome<ReviewTrialResult>.Refused(new Refusal(
                "review.measurement-cancelled", FailureReason.Refused, "The check was cancelled."));
        }

        var recorded = await Task.Run(() => JobCommands.Assessments(new JobAssessmentsRequest(
            request.ProjectPath, jobId, MotifProductVersion.CurrentText)));
        if (!recorded.Succeeded) return CommandOutcome<ReviewTrialResult>.Refused(recorded.Refusal!);
        var correctness = recorded.Value!.Assessments.LastOrDefault(item => item.Kind == "Correctness");
        if (correctness is null) return RefuseTrial("The check did not measure approved analyses.");
        var numbers = await Task.Run(() => ReviewNumbersCommand.Read(new ReviewNumbersCommand.Request(
            request.ProjectPath, request.CurrentCorrectnessAssessmentId, correctness.AssessmentId,
            request.Words.Count)));
        if (!numbers.Succeeded) return CommandOutcome<ReviewTrialResult>.Refused(numbers.Refusal!);
        progress.Report(new ReviewTrialProgress(request.Words.Count, request.Words.Count, null));
        return CommandOutcome<ReviewTrialResult>.Success(new ReviewTrialResult(
            jobId, request.Revision, numbers.Value!.Text, numbers.Value.EvidenceComplete));
    }

    private static CommandOutcome<ReviewTrialResult> RefuseTrial(string message) =>
        CommandOutcome<ReviewTrialResult>.Refused(new Refusal(
            "review.measurement-incomplete", FailureReason.Refused, message));

    public Task<CommandOutcome<PendingChangesSnapshot>> LoadPendingChangesAsync(
        PendingChangesRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Load(request));

    public Task<CommandOutcome<PendingChangesSnapshot>> PutPendingChangeAsync(
        PutPendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Put(request));

    public Task<CommandOutcome<PendingChangesSnapshot>> RemovePendingChangeAsync(
        RemovePendingChangeRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => PendingChanges.Remove(request));
}
