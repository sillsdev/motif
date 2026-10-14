using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host;
using SIL.Motif.Runner.Composers;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Applies and measures pending changes through the catalog's UI-independent workflow.</summary>
public static class PendingChangesWorkflow
{
    /// <summary>
    /// Applies the pending changes through the one Apply path, at FieldWorks' save boundary. A project carrying an
    /// unresolved <see cref="ProjectReconciliationMarker"/> is refused with <c>apply.reconciliation-needed</c>
    /// before anything is read or changed, until the warning is cleared by reconciling the project in Motif.
    /// </summary>
    /// <param name="request">The project, the pending Draft and revision the caller reviewed, if any.</param>
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
        if (ProjectReconciliationMarker.Exists(request.ProjectPath))
        {
            return CommandOutcome<ApplyPendingResult>.Refused(new Refusal("apply.reconciliation-needed",
                FailureReason.StoreInconsistent,
                "Motif could not confirm that an earlier Apply was saved to this project. Open Motif to check " +
                "the project. Do not retry automatically."));
        }
        var identity = ReadPendingIdentity(request.ProjectPath);
        if (!identity.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(identity.Refusal!);
        if (identity.Value!.Current is not { } current)
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending);
        if (request.DraftId is { } expectedDraft && expectedDraft != current.DraftId)
            return RefuseApply("apply.changes-changed", ChangesChanged(isPending: true));
        return ApplyOne(new ApplyTarget(request.ProjectPath, current.DraftId, request.Revision, Force: false,
            DryRunSource.Fresh), cancellationToken, dryRunTimeout, runnerLauncher);
    }

    /// <summary>
    /// Applies one Proposal through the same path as the pending changes: finalize a Draft, check every change
    /// once, take its Dry Run, apply in one unit of work, and reopen the Draft under the same id on refusal.
    /// </summary>
    /// <param name="request">The project, the Proposal, and the revision the caller reviewed.</param>
    /// <param name="cancellationToken">Cancels the wait for the project to be released.</param>
    /// <remarks>
    /// The Proposal, the pending changes included, takes the Dry Run of its current Trial and is refused when it
    /// has none; only <c>apply --all-pending</c>, at FieldWorks' save boundary, queues a fresh one.
    /// </remarks>
    public static CommandOutcome<ApplyPendingResult> Apply(
        ApplyProposalRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApplyOne(new ApplyTarget(request.ProjectPath, request.ProposalId, request.ExpectedRevision,
            Force: false, DryRunSource.Trial), cancellationToken, null, null);
    }

    /// <summary>
    /// The developer <c>apply &lt;id&gt;</c>: the same path, taking the Dry Run already bound by <c>dryrun</c>
    /// and, with <see cref="ApplyRequest.Force"/>, overriding Readiness.
    /// </summary>
    /// <param name="request">The project, the Proposal, and whether Readiness is overridden.</param>
    public static CommandOutcome<ApplyPendingResult> Apply(ApplyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ApplyOne(new ApplyTarget(request.FwDataPath, request.ProposalId, null, request.Force,
            DryRunSource.Bound), CancellationToken.None, null, null);
    }

    // Lets a test run a second Apply between this one's reads and its Finalize, the window two processes share.
    internal static readonly AsyncLocal<Action?> BeforeFinalizeForTests = new();

    private sealed record ApplyTarget(string ProjectPath, string ProposalId, string? ExpectedRevision, bool Force,
        DryRunSource DryRun);

    // Where the Dry Run Apply binds comes from; it is the only thing the front doors choose.
    private enum DryRunSource
    {
        // FieldWorks' save boundary: a Dry Run queued now, so the pending changes need no Trial.
        Fresh,

        // A reviewed Proposal: the Dry Run of its current Trial.
        Trial,

        // The developer verb: the Dry Run `dryrun` already bound.
        Bound,
    }

    // The one Apply orchestration; every front door above only names the Proposal and its Dry Run source.
    private static CommandOutcome<ApplyPendingResult> ApplyOne(ApplyTarget target,
        CancellationToken cancellationToken, TimeSpan? dryRunTimeout, IJobRunnerLauncher? runnerLauncher)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A missing project has no folder to hold a lease in.
        if (!File.Exists(target.ProjectPath))
            return ApplyLeased(target, cancellationToken, dryRunTimeout, runnerLauncher);
        using var lease = ProjectApplyLease.TryAcquire(target.ProjectPath);
        if (lease is null)
        {
            return CommandOutcome<ApplyPendingResult>.Refused(new Refusal("apply.in-progress", FailureReason.Busy,
                "Another Apply is already running for this project. Try again when it finishes.",
                new Dictionary<string, string>(StringComparer.Ordinal) { ["fwDataPath"] = target.ProjectPath }));
        }
        return ApplyLeased(target, cancellationToken, dryRunTimeout, runnerLauncher);
    }

    private static CommandOutcome<ApplyPendingResult> ApplyLeased(ApplyTarget target,
        CancellationToken cancellationToken, TimeSpan? dryRunTimeout, IJobRunnerLauncher? runnerLauncher)
    {
        var version = MotifProductVersion.CurrentText;
        var read = ProjectStoreCommand.Run(target.ProjectPath, version, (database, project) =>
            ReadApplyIdentity(database, project, target));
        if (!read.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(read.Refusal!);
        var identity = read.Value!;
        if (identity.NothingToApply) return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.NothingPending);

        // FieldWorks' lock is refused here, before any Draft is finalized, checked or dry-run.
        var released = ProjectStoreCommand.Run(target.ProjectPath, version, (_, project) =>
        {
            using var cache = new FwDataProjectLoader().LoadScratchCache(project.FullFwDataPath);
            return CommandOutcome<ApplyIdentity>.Success(identity);
        });
        if (!released.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(released.Refusal!);

        // Every change is checked once, on the content as read, so a change that no longer fits finalizes nothing.
        var fit = ProposalCommands.Preflight(new PreflightRequest(target.ProjectPath, version, identity.ProposalId),
            identity.Content);
        if (!fit.Succeeded) return CommandOutcome<ApplyPendingResult>.Refused(fit.Refusal!);
        var uncertainOperations = fit.Value!.Changes
            .Where(operation => operation.Status == ChangeFitStatus.Uncertain)
            .Select(operation => operation.ChangeId ?? operation.OperationId).ToArray();
        if (uncertainOperations.Length > 0)
            return CommandOutcome<ApplyPendingResult>.Refused(PendingChangeRefusals.Uncertain(uncertainOperations));
        if (fit.Value.Changes.Any(operation => !operation.StillFits))
            return RefuseApply("apply.change-no-longer-fits",
                "One or more changes no longer fit the project. Remove those changes first.");

        BeforeFinalizeForTests.Value?.Invoke();
        string proposalId = identity.ProposalId;
        if (identity.DraftName is { } draftName)
        {
            var finalized = ProposalCommands.Finalize(new FinalizeRequest(target.ProjectPath, version, draftName,
                identity.Revision));
            if (!finalized.Succeeded)
            {
                if (finalized.Refusal?.Code == "draft.revision-conflict")
                    return RefuseApply("apply.changes-changed", ChangesChanged(identity.IsPendingChanges));
                return CommandOutcome<ApplyPendingResult>.Refused(finalized.Refusal!);
            }
            proposalId = finalized.Value!.ProposalId;
        }

        var unbound = target.DryRun switch
        {
            DryRunSource.Fresh => RunFreshDryRun(target.ProjectPath, version, proposalId, cancellationToken,
                dryRunTimeout, runnerLauncher),
            DryRunSource.Trial => BindTrialDryRun(target.ProjectPath, version, proposalId),
            _ => null,
        };
        if (unbound is not null) return ReopenAfterRefusal(target.ProjectPath, identity, unbound);

        var applied = ProposalCommands.Apply(new ApplyRequest(target.ProjectPath, version, proposalId, target.Force));
        if (applied.Succeeded)
            return CommandOutcome<ApplyPendingResult>.Success(ApplyPendingResult.AppliedWith(applied.Value!,
                identity.Summary!));
        if (applied.Refusal?.Code == "apply.reconciliation-needed")
            return CommandOutcome<ApplyPendingResult>.Refused(applied.Refusal!);
        return ReopenAfterRefusal(target.ProjectPath, identity, applied.Refusal!);
    }

    private static CommandOutcome<ApplyIdentity> ReadApplyIdentity(MotifDatabase database, ProjectLocator project,
        ApplyTarget target)
    {
        var repository = new ProposalRepository(database);
        ProposalRecord record;
        string id;
        try
        {
            id = ProposalCommands.NormalizeId(target.ProposalId);
            record = repository.Get(CanonicalId.Parse(id));
        }
        catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
        {
            return CommandOutcome<ApplyIdentity>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
        }
        var isDraft = record.DraftName is not null;
        var isPending = isDraft && record.IsPendingChanges;
        string revision;
        DraftDocument draft;
        if (isDraft)
        {
            draft = ProposalCommands.DeserializeDraft(record.ProposalJson!);
            revision = DraftRevision.Compute(record.ProposalJson);
        }
        else
        {
            var (finalized, envelope) = repository.GetFinalized(record.ProposalId);
            draft = ProposalCommands.ToDraftDocument(id, envelope, finalized.Label, finalized.Comment);
            revision = finalized.IntentDigest!;
        }
        if (target.ExpectedRevision is { } expected && expected != revision)
            return CommandOutcome<ApplyIdentity>.Refused(new Refusal("apply.changes-changed",
                FailureReason.Refused, ChangesChanged(isPending)));
        if (draft.Operations.Count == 0)
            return CommandOutcome<ApplyIdentity>.Success(new ApplyIdentity(id, record.DraftName, revision, null,
                true, isPending, record.ProposalJson));
        if (target.DryRun == DryRunSource.Trial && CheckTrial(database, project, record.ProposalId) is { } refusal)
            return CommandOutcome<ApplyIdentity>.Refused(refusal);
        var changes = PendingChanges.ReadChanges(draft);
        var summary = changes.Length > 0 || isPending ? SummaryOf(changes)
            : $"Applied {(string.IsNullOrWhiteSpace(record.Label) ? "the Proposal" : record.Label)}.";
        return CommandOutcome<ApplyIdentity>.Success(new ApplyIdentity(id, record.DraftName, revision, summary,
            false, isPending, isDraft ? record.ProposalJson : null,
            ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft))));
    }

    private static Refusal? RunFreshDryRun(string projectPath, string version, string proposalId,
        CancellationToken cancellationToken, TimeSpan? dryRunTimeout, IJobRunnerLauncher? runnerLauncher)
    {
        var queued = JobCommands.EnqueueDryRun(new EnqueueDryRunRequest(projectPath, version, proposalId));
        if (!queued.Succeeded) return queued.Refusal!;
        (runnerLauncher ?? ProcessRunnerLauncher.FromEnvironment()).Start(projectPath);
        var dryRun = JobCommands.WaitForDryRun(new WaitForDryRunRequest(projectPath, version, proposalId,
            queued.Value!.JobId, dryRunTimeout ?? JobCommands.DefaultWaitTimeout), cancellationToken,
            cancelOnTimeout: true);
        return dryRun.Succeeded ? null : dryRun.Refusal!;
    }

    private static Refusal? BindTrialDryRun(string projectPath, string version, string proposalId) =>
        ProjectStoreCommand.Run(projectPath, version, (database, project) =>
        {
            var canonical = CanonicalId.Parse(proposalId);
            var refusal = CheckTrial(database, project, canonical);
            if (refusal is not null) return CommandOutcome<string>.Refused(refusal);
            var trial = Trials.LatestFor(database, project, proposalId)!;
            var job = new JobRepository(database).Get(trial.JobId);
            if (job?.DryRunJson is null)
                return CommandOutcome<string>.Refused(TrialMissing());
            var dryRun = JobCommands.ParsePublishedDryRun(job.DryRunJson);
            if (dryRun.IntentDigest != trial.IntentDigest)
                return CommandOutcome<string>.Refused(TrialStale());
            new ProposalRepository(database).SetAnchor(canonical, JsonSerializer.Serialize(dryRun.Anchor));
            return CommandOutcome<string>.Success("bound");
        }).Refusal;

    private static Refusal? CheckTrial(MotifDatabase database, ProjectLocator project, CanonicalId canonical)
    {
        var trial = Trials.LatestFor(database, project, canonical.Value);
        if (trial is null || trial.Status != JobStatus.Completed) return TrialMissing();
        return trial.Freshness == TrialFreshness.Current ? null : TrialStale();
    }

    private static Refusal TrialMissing() => new("apply.trial-missing", FailureReason.Refused,
        "This Proposal has not been tried. Try it before applying.");

    private static Refusal TrialStale() => new("apply.trial-stale", FailureReason.Refused,
        "This Proposal or the project changed since it was tried. Try it again before applying.");

    // The window never calls the pending changes a Proposal.
    private static string ChangesChanged(bool isPending) => isPending
        ? "The changes changed. Reload and check them before applying."
        : "The Proposal changed. Reload and check it before applying.";

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

        var identity = await Task.Run(() => request.DraftId is { } named
            ? ReadDraftIdentity(request.ProjectPath, named)
            : ReadPendingIdentity(request.ProjectPath)).ConfigureAwait(false);
        if (!identity.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(identity.Refusal!);
        if (identity.Value!.Current is not { } current)
            return RefuseMeasure("trial.nothing-pending", "There are no pending changes to measure.");
        if (request.Revision is { } expectedRevision && expectedRevision != current.Revision)
            return RefuseMeasure("trial.changes-changed", "The changes changed. Reload them before measuring.");

        TrialLimits? limits = null;
        if (request.BeforeCorrectnessAssessmentId is { } beforeAssessmentId)
        {
            var earlier = await Task.Run(() => ReadTrialLimits(request.ProjectPath, beforeAssessmentId))
                .ConfigureAwait(false);
            if (!earlier.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(earlier.Refusal!);
            limits = earlier.Value;
        }

        var queued = await Task.Run(() => JobCommands.EnqueueTrial(new EnqueueTrialRequest(
            request.ProjectPath, version, current.DraftId, Words: request.Words,
            ExpectedDraftRevision: current.Revision, Limits: limits))).ConfigureAwait(false);
        if (queued.Refusal?.Code == "draft.revision-conflict")
            return RefuseMeasure("trial.changes-changed", "The changes changed. Reload them before measuring.");
        if (!queued.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(queued.Refusal!);
        var jobId = queued.Value!.JobId;
        (runnerLauncher ?? ProcessRunnerLauncher.FromEnvironment()).Start(request.ProjectPath);

        MeasureProgress? lastProgress = null;
        var jobProgress = new JobStatusProgress(status =>
        {
            if (status.TrialProgress is not { } wordProgress) return;
            var next = new MeasureProgress(wordProgress.Completed, wordProgress.Total) { WordProgress = wordProgress };
            if (lastProgress?.WordProgress is { } last && last.Completed == wordProgress.Completed &&
                last.CurrentWord == wordProgress.CurrentWord && last.StoppedWords.Count == wordProgress.StoppedWords.Count)
                return;
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
        var trial = await Task.Run(() => ProjectStoreCommand.Run(request.ProjectPath, version, (database, project) =>
        {
            var record = Trials.LatestFor(database, project, current.DraftId);
            return record is null
                ? CommandOutcome<TrialRecord>.Refused(new Refusal("trial.measurement-incomplete",
                    FailureReason.Refused, "The Trial record could not be read. Try it again."))
                : CommandOutcome<TrialRecord>.Success(record);
        })).ConfigureAwait(false);
        if (!trial.Succeeded) return CommandOutcome<MeasurePendingResult>.Refused(trial.Refusal!);
        if (trial.Value!.JobId != jobId)
            return RefuseMeasure("trial.measurement-incomplete", "The Trial record could not be read. Try it again.");
        progress.Report(new MeasureProgress(request.Words.Count, request.Words.Count));
        return CommandOutcome<MeasurePendingResult>.Success(new MeasurePendingResult(
            trial.Value, numbers.Value!) { Revision = current.Revision });
    }

    // The one definition of "nothing pending" for both workflows: no Draft, or a Draft with no operations.
    private static CommandOutcome<PendingDraftRead> ReadPendingIdentity(string projectPath) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var draft = new ProposalRepository(database).FindPendingChangesDraft();
            if (draft?.ProposalJson is not { } json || JsonSerializer.Deserialize<DraftDocument>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Operations.Count is null or 0)
                return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(null));
            return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(new PendingDraftIdentity(
                draft.ProposalId.Value, DraftRevision.Compute(json))));
        });

    // A named Draft is measured like the pending one, through the same Trial; one with no operations has nothing.
    private static CommandOutcome<PendingDraftRead> ReadDraftIdentity(string projectPath, string proposalId) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            ProposalRecord record;
            try { record = new ProposalRepository(database).Get(CanonicalId.Parse(ProposalCommands.NormalizeId(proposalId))); }
            catch (Exception exception) when (exception is ArgumentException or KeyNotFoundException)
            {
                return CommandOutcome<PendingDraftRead>.Refused(new Refusal(RefusalCodes.TrialChangesChanged,
                    FailureReason.Refused, "The changes changed. Reload them before measuring."));
            }
            if (record.DraftName is null)
                return CommandOutcome<PendingDraftRead>.Refused(new Refusal(RefusalCodes.TrialChangesChanged,
                    FailureReason.Refused, "The changes changed. Reload them before measuring."));
            if (record.ProposalJson is not { } json ||
                JsonSerializer.Deserialize<DraftDocument>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))?.Operations.Count is null or 0)
                return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(null));
            return CommandOutcome<PendingDraftRead>.Success(new PendingDraftRead(new PendingDraftIdentity(
                record.ProposalId.Value, DraftRevision.Compute(json))));
        });

    private static CommandOutcome<TrialLimits> ReadTrialLimits(string projectPath, string assessmentId) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            AssessmentRecord assessment;
            try { assessment = repository.Get(assessmentId); }
            catch (KeyNotFoundException)
            {
                return CommandOutcome<TrialLimits>.Refused(new Refusal("review.assessment-not-found",
                    FailureReason.NotFound, $"No Assessment is recorded as '{assessmentId}'."));
            }
            if (assessment.Kind != RegressionChecker.RequiredKind)
                return CommandOutcome<TrialLimits>.Refused(new Refusal("review.wrong-assessment-kind",
                    FailureReason.InvalidArgument, "Review numbers require Correctness Assessments."));
            try
            {
                var scope = ScopeCodec.ReadTrial(assessment.ScopeJson, RegressionChecker.RequiredKind);
                return CommandOutcome<TrialLimits>.Success(new TrialLimits(
                    scope.PerWordLimit, scope.PerWordStepLimit));
            }
            catch (ReportRefusalException exception)
            {
                return CommandOutcome<TrialLimits>.Refused(new Refusal("review.numbers-unavailable",
                    FailureReason.Refused, exception.Message));
            }
        });

    // A Draft that Apply finalized is reopened exactly as it was read, so its revision is unchanged for the caller.
    private static CommandOutcome<ApplyPendingResult> ReopenAfterRefusal(
        string projectPath, ApplyIdentity identity, Refusal reason)
    {
        if (identity.DraftName is not { } draftName || identity.DraftJson is not { } draftJson)
            return CommandOutcome<ApplyPendingResult>.Refused(reason);
        var reopened = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                new ProposalRepository(database).ReopenAsDraft(CanonicalId.Parse(identity.ProposalId), draftName,
                    draftJson);
                return CommandOutcome<string>.Success(draftName);
            }
            catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException)
            {
                return CommandOutcome<string>.Refused(new Refusal("apply.reopen-failed", FailureReason.Refused,
                    exception.Message));
            }
        });
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

    private sealed record ApplyIdentity(string ProposalId, string? DraftName, string Revision, string? Summary,
        bool NothingToApply, bool IsPendingChanges, string? DraftJson = null,
        SIL.Motif.Contract.Model.Proposal? Content = null);

    private sealed record PendingDraftIdentity(string DraftId, string Revision);

    private sealed record PendingDraftRead(PendingDraftIdentity? Current);

    private sealed class JobStatusProgress(Action<JobStatusResponse> report) : IProgress<JobStatusResponse>
    {
        public void Report(JobStatusResponse value) => report(value);
    }
}
