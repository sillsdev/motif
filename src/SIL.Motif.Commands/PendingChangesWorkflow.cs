using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Applies and measures pending changes through the catalog's UI-independent workflow.</summary>
public static class PendingChangesWorkflow
{
    /// <summary>Finalizes, checks, evaluates, and applies the pending changes as one workflow.</summary>
    /// <param name="request">The project, pending Draft, expected revision, and applying user.</param>
    /// <param name="cancellationToken">Cancels the queued Dry Run and reopens the Draft while waiting.</param>
    /// <param name="dryRunTimeout">The wait bound, or <see langword="null"/> to use the command default.</param>
    /// <param name="runnerLauncher">
    /// Starts the runner for the queued Dry Run, or <see langword="null"/> for
    /// <see cref="ProcessRunnerLauncher.FromEnvironment"/>, the command line's own.
    /// </param>
    public static CommandOutcome<ApplyPendingResult> Apply(
        ApplyPendingRequest request, CancellationToken cancellationToken = default,
        TimeSpan? dryRunTimeout = null, IJobRunnerLauncher? runnerLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var version = MotifProductVersion.CurrentText;
        var identity = ReadPendingIdentity(request.ProjectPath);
        if (!identity.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(identity.Refusal!);
        if (identity.Value!.Current is null)
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending);
        var pending = PendingChanges.Load(new PendingChangesRequest(request.ProjectPath, version));
        if (!pending.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(pending.Refusal!);
        var snapshot = pending.Value!;
        if (snapshot.DraftId is not { } draftId ||
            request.DraftId is { } expectedDraft && expectedDraft != draftId ||
            request.Revision is { } expectedRevision && expectedRevision != snapshot.Revision)
            return RefuseApply("apply.changes-changed",
                "The changes changed. Reload and check them before applying.");
        var uncertain = snapshot.FitSummary.Where(fit => fit.Status == ChangeFitStatus.Uncertain)
            .Select(fit => fit.ChangeId).ToArray();
        if (uncertain.Length > 0)
            return CommandOutcome<ApplyPendingResult>.Refused(PendingChangeRefusals.Uncertain(uncertain));
        if (snapshot.FitSummary.Count != snapshot.Changes.Count || snapshot.FitSummary.Any(fit => !fit.StillFits))
            return RefuseApply("apply.change-no-longer-fits",
                "One or more changes no longer fit the project. Remove those changes first.");
        var resolvedRequest = request with { DraftId = draftId, Revision = snapshot.Revision };

        // Original ownership is required before Apply finalizes changes or queues its Dry Run.
        var released = ProjectStoreCommand.Run(request.ProjectPath, version, (_, project) =>
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
            return CommandOutcome<PendingChangesSnapshot>.Success(snapshot);
        });
        if (!released.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(released.Refusal!);

        var finalized = ProposalCommands.Finalize(new FinalizeRequest(
            request.ProjectPath, version, PendingChanges.DraftName, snapshot.Revision));
        if (!finalized.Succeeded)
        {
            if (finalized.Refusal?.Code == "draft.revision-conflict")
                return RefuseApply("apply.changes-changed",
                    "The changes changed. Reload and check them before applying.");
            return CommandOutcome<ApplyPendingResult>.Refused(finalized.Refusal!);
        }
        var proposalId = finalized.Value!.ProposalId;

        var fit = ProposalCommands.Preflight(new PreflightRequest(request.ProjectPath, version, proposalId));
        if (!fit.Succeeded) return ReopenAfterRefusal(resolvedRequest, fit.Refusal!);
        var uncertainOperations = fit.Value!.Changes
            .Where(operation => operation.Status == ChangeFitStatus.Uncertain)
            .Select(operation => operation.ChangeId ?? operation.OperationId).ToArray();
        if (uncertainOperations.Length > 0)
            return ReopenAfterRefusal(resolvedRequest, PendingChangeRefusals.Uncertain(uncertainOperations));
        if (fit.Value.Changes.Any(operation => !operation.StillFits))
            return ReopenAfterRefusal(resolvedRequest, new Refusal("apply.change-no-longer-fits",
                FailureReason.Refused, "One or more changes no longer fit the project. Remove those changes first."));

        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(request.ProjectPath, version, proposalId));
        if (!queued.Succeeded) return ReopenAfterRefusal(resolvedRequest, queued.Refusal!);
        (runnerLauncher ?? ProcessRunnerLauncher.FromEnvironment()).Start(request.ProjectPath);
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(
            request.ProjectPath, version, proposalId, queued.Value!.JobId,
            dryRunTimeout ?? JobCommands.DefaultWaitTimeout), cancellationToken, cancelOnTimeout: true);
        if (!dryRun.Succeeded) return ReopenAfterRefusal(resolvedRequest, dryRun.Refusal!);

        var applied = ProposalCommands.Apply(new ApplyRequest(
            request.ProjectPath, version, proposalId, request.User));
        if (applied.Succeeded)
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(
                applied.Value!, SummaryOf(snapshot.Changes)));
        if (applied.Refusal?.Code == "apply.reconciliation-needed")
            return CommandOutcome<ApplyPendingResult>.Refused(applied.Refusal!);
        return ReopenAfterRefusal(resolvedRequest, applied.Refusal!);
    }

    /// <summary>Runs a Trial for the selected words and returns its recorded correctness evidence.</summary>
    /// <param name="request">The project, pending Draft, revision, words, and earlier Assessment identity.</param>
    /// <param name="progress">Receives the number of words completed out of those requested.</param>
    /// <param name="cancellationToken">Cancels the Trial job and stops waiting for it.</param>
    /// <param name="waitTimeout">The wait bound, or <see langword="null"/> to wait until completion or cancellation.</param>
    /// <param name="runnerLauncher">
    /// Starts the runner for the queued Trial, or <see langword="null"/> for
    /// <see cref="ProcessRunnerLauncher.FromEnvironment"/>, the command line's own.
    /// </param>
    public static async Task<CommandOutcome<MeasurePendingResult>> Measure(
        MeasurePendingRequest request,
        IProgress<MeasureProgress> progress,
        CancellationToken cancellationToken,
        TimeSpan? waitTimeout = null,
        IJobRunnerLauncher? runnerLauncher = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(progress);
        progress.Report(new MeasureProgress(0, request.Words.Count));
        var version = MotifProductVersion.CurrentText;
        if (cancellationToken.IsCancellationRequested) return CancelledMeasure();

        var identity = await Task.Run(() => ReadPendingIdentity(request.ProjectPath)).ConfigureAwait(false);
        if (!identity.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(identity.Refusal!);
        if (identity.Value!.Current is not { } current)
            return RefuseMeasure("trial.nothing-pending", "There are no pending changes to measure.");
        if (request.DraftId is { } expectedDraft && expectedDraft != current.DraftId ||
            request.Revision is { } expectedRevision && expectedRevision != current.Revision)
            return RefuseMeasure("trial.changes-changed", "The changes changed. Reload them before measuring.");

        var queued = await Task.Run(() => JobCommands.EnqueueTrial(new EnqueueTrialRequest(
            request.ProjectPath, version, current.DraftId, Words: request.Words,
            ExpectedDraftRevision: current.Revision))).ConfigureAwait(false);
        if (queued.Refusal?.Code == "draft.revision-conflict")
            return RefuseMeasure("trial.changes-changed", "The changes changed. Reload them before measuring.");
        if (!queued.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(queued.Refusal!);
        var jobId = queued.Value!.JobId;
        (runnerLauncher ?? ProcessRunnerLauncher.FromEnvironment()).Start(request.ProjectPath);

        MeasureProgress? lastProgress = null;
        var jobProgress = new JobStatusProgress(status =>
        {
            if (status.TrialProgress is not { } wordProgress) return;
            var next = new MeasureProgress(wordProgress.Completed, wordProgress.Total);
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
        var correctness = recorded.Value!.Assessments.LastOrDefault(item => item.Kind == AssessmentKinds.Correctness);
        if (correctness is null)
            return RefuseMeasure("trial.measurement-incomplete", "The check did not measure approved analyses.");
        var numbers = await Task.Run(() => ReviewNumbersCommand.Read(new ReviewNumbersCommand.Request(
            request.ProjectPath, request.BeforeCorrectnessAssessmentId, correctness.AssessmentId,
            request.Words.Count))).ConfigureAwait(false);
        if (!numbers.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(numbers.Refusal!);
        progress.Report(new MeasureProgress(request.Words.Count, request.Words.Count));
        return CommandOutcome<MeasurePendingResult>.Success(new MeasurePendingResult(
            jobId, current.Revision, numbers.Value!));
    }

    // The one definition of "nothing pending" for both workflows: no Draft, or a Draft with no operations.
    private static CommandOutcome<PendingDraftRead> ReadPendingIdentity(string projectPath) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new ProposalRepository(database);
            var draft = repository.DraftNameExists(PendingChanges.DraftName)
                ? repository.GetDraft(PendingChanges.DraftName) : null;
            if (draft?.ProposalJson is not { } json || JsonSerializer.Deserialize<DraftDocument>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Operations.Count is null or 0)
                return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(null));
            return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(new PendingDraftIdentity(
                draft.ProposalId.Value, DraftRevision.Compute(json))));
        });

    private static CommandOutcome<ApplyPendingResult> ReopenAfterRefusal(
        ApplyPendingRequest request, Refusal reason)
    {
        var reopened = ProposalCommands.Reopen(new ReopenRequest(
            request.ProjectPath, MotifProductVersion.CurrentText, PendingChanges.DraftName, request.DraftId!));
        return reopened.Succeeded ? CommandOutcome<ApplyPendingResult>.Refused(reason)
            : RefuseApply("apply.reopen-failed",
                reason.Message + " The changes could not be reopened: " + reopened.Refusal!.Message);
    }

    private static CommandOutcome<ApplyPendingResult> RefuseApply(string code, string message) =>
        CommandOutcome<ApplyPendingResult>.Refused(new Refusal(code, FailureReason.Refused, message));

    private static string SummaryOf(IReadOnlyList<PendingChange> changes)
    {
        var parts = new List<string>();
        Add(AnalysisChangeKinds.Approve, "Approved", "analysis", "analyses");
        Add(AnalysisChangeKinds.Reject, "Disapproved", "analysis", "analyses");
        var returned = Count(AnalysisChangeKinds.Candidate);
        if (returned > 0)
            parts.Add($"Set {Counted(returned, "analysis", "analyses")} to Unknown");
        Add(AnalysisChangeKinds.AddCandidate, "Added", "analysis as Unknown", "analyses as Unknown");
        Add(AnalysisChangeKinds.IncorrectSpelling, "Marked", "word as incorrectly spelled",
            "words as incorrectly spelled");
        return parts.Count == 0 ? "Applied pending changes." : string.Join(", ", parts) + ".";

        void Add(string kind, string verb, string singular, string plural)
        {
            var count = Count(kind);
            if (count > 0) parts.Add($"{verb} {Counted(count, singular, plural)}");
        }

        int Count(string kind) => changes.Count(change => change.Kind == kind);
    }

    private static string Counted(int count, string singular, string plural) =>
        $"{count} {(count == 1 ? singular : plural)}";

    private static CommandOutcome<MeasurePendingResult> RefuseMeasure(string code, string message) =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            code, FailureReason.Refused, message));

    private static CommandOutcome<MeasurePendingResult> CancelledMeasure() =>
        CommandOutcome<MeasurePendingResult>.Refused(new Refusal(
            "job.wait-cancelled", FailureReason.Cancelled, "The check was cancelled."));

    private sealed record PendingDraftIdentity(string DraftId, string Revision);

    private sealed record PendingDraftRead(PendingDraftIdentity? Current);

    private sealed class JobStatusProgress(Action<JobStatusResponse> report) : IProgress<JobStatusResponse>
    {
        public void Report(JobStatusResponse value) => report(value);
    }
}
