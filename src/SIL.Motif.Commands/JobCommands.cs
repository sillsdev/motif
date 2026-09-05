using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Model.DryRun;
using SIL.Motif.Model.Effects;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Usage;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using DryRunModel = SIL.Motif.Model.DryRun.DryRun;

namespace SIL.Motif.Commands;

/// <summary>
/// The verbs that put durable work in the queue and read back what became of it.
/// </summary>
/// <remarks>
/// Both open the paired database directly, like every other verb. Enqueueing returns as soon as the row
/// is committed rather than waiting for the work, because the runner that will do it is a different
/// process and may not even be started yet — the row is the whole handoff.
/// </remarks>
public static class JobCommands
{
    /// <summary>The durable kind a queued Baseline refresh carries.</summary>
    public const string BaselineRefreshKind = "baseline-refresh";

    /// <summary>The durable kind a queued Dry Run carries.</summary>
    public const string DryRunKind = "dry-run";

    /// <summary>The durable kind a queued Trial carries.</summary>
    public const string TrialKind = "trial";

    /// <summary><c>--wait</c>'s default bound before it gives up and reports the job still unfinished.</summary>
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan WaitPollInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Queues a Baseline refresh for one project and returns the job id that names it.</summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueBaselineRefresh(EnqueueBaselineRefreshRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var created = jobs.Create(jobId, workspaceKey, BaselineRefreshKind,
                "{}", NowStamp());
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, BaselineRefreshKind, workspaceKey));
        });
    }

    /// <summary>
    /// Loads and validates the named Proposal through the same <see cref="ProposalRepository.GetFinalized"/>
    /// path <c>show</c> and <c>apply</c> use, refusing before any row is queued when it is absent or
    /// inconsistent, then queues a Dry Run job and returns the job id that names it.
    /// </summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueDryRun(
        EnqueueDryRunRequest request, UsageLog? usage = null)
    {
        usage?.Record(DryRunKind,
            new[] { UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("proposalId") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                (record, _) = repository.GetFinalized(CanonicalId.Parse(id));
            }
            catch (Exception exception)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }

            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var created = jobs.Create(jobId, workspaceKey, DryRunKind, record.ProposalJson!, NowStamp());
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, DryRunKind, workspaceKey));
        });
    }

    /// <summary>
    /// Loads one Proposal — a committed revision or an uncommitted Draft, either resolves — through
    /// <see cref="ProposalRepository.Get"/>, refusing before any row is queued when it is absent, then
    /// queues a Trial job and returns the job id that names it.
    /// </summary>
    public static CommandOutcome<JobEnqueuedResponse> EnqueueTrial(EnqueueTrialRequest request, UsageLog? usage = null)
    {
        usage?.Record(TrialKind,
            new[] { UsageArgumentShape.Text("fwDataPath"), UsageArgumentShape.Text("proposalId") });
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var repository = new ProposalRepository(database);
            ProposalRecord record;
            try
            {
                var id = ProposalCommands.NormalizeId(request.ProposalId);
                record = repository.Get(CanonicalId.Parse(id));
            }
            catch (Exception exception)
            {
                return CommandOutcome<JobEnqueuedResponse>.Refused(ProposalCommands.ProposalLoadRefusal(exception));
            }

            var jobs = new JobRepository(database);
            var jobId = CanonicalId.Mint("job/").Value;
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var inputJson = JsonSerializer.Serialize(
                new TrialJobInput(record.ProposalJson!, request.Scope), MotifJson.CreateOptions());
            var created = jobs.Create(jobId, workspaceKey, TrialKind, inputJson, NowStamp());
            return CommandOutcome<JobEnqueuedResponse>.Success(
                new JobEnqueuedResponse(created.JobId, TrialKind, workspaceKey));
        });
    }

    /// <summary>
    /// Polls one Dry Run job until it reaches a terminal state, binds the published anchor onto the
    /// Proposal exactly as the in-process verb used to (so <c>apply</c> keeps working), and returns it.
    /// A job still not terminal when <see cref="WaitForDryRunRequest.Timeout"/> elapses is reported as its
    /// own distinct refusal rather than as though the Dry Run had failed.
    /// </summary>
    public static CommandOutcome<DryRunProjection> WaitForDryRun(WaitForDryRunRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            var jobs = new JobRepository(database);
            var deadline = DateTimeOffset.UtcNow + request.Timeout;
            JobRecord? job;
            while (true)
            {
                job = jobs.Get(request.JobId);
                if (job is null) return CommandOutcome<DryRunProjection>.Refused(JobNotFound(request.JobId));
                if (JobStateMachine.IsTerminal(job.Status)) break;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return CommandOutcome<DryRunProjection>.Refused(new Refusal(
                        "job.wait-timeout", FailureReason.Busy,
                        "Timed out after " + request.Timeout + " waiting for Dry Run job '" + request.JobId +
                        "' to finish; it is still " + JobStatusJson.ToWire(job.Status) +
                        ". Check again with 'jobs show " + request.JobId + " --project <fwdata>'.",
                        Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(job.Status)))));
                }
                Thread.Sleep(WaitPollInterval);
            }

            if (job.Status != JobStatus.CompletedDryRunOnly || job.DryRunJson is null)
            {
                return CommandOutcome<DryRunProjection>.Refused(new Refusal(
                    "job.dry-run-incomplete", FailureReason.Refused,
                    "Dry Run job '" + request.JobId + "' finished as " + JobStatusJson.ToWire(job.Status) +
                    " rather than completing.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(job.Status)))));
            }

            var repository = new ProposalRepository(database);
            var id = ProposalCommands.NormalizeId(request.ProposalId);
            var canonicalId = CanonicalId.Parse(id);
            var dryRun = ParsePublishedDryRun(job.DryRunJson);

            // Persist the bound-DryRun anchor (docs/adr/0004 decision 3): apply requires it present and unmoved.
            repository.SetAnchor(canonicalId, JsonSerializer.Serialize(dryRun.Anchor));

            return CommandOutcome<DryRunProjection>.Success(DryRunProjectionBuilder.Build(id, dryRun));
        });
    }

    /// <summary>
    /// Polls one job until it reaches a terminal state, then returns it exactly as <see cref="Show"/>
    /// does — used by verbs, such as <c>trial</c>, whose completion has no Dry-Run-specific anchor to
    /// bind and so needs no projection of its own beyond the job's own terminal status.
    /// </summary>
    public static CommandOutcome<JobStatusResponse> WaitForJob(WaitForJobRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var deadline = DateTimeOffset.UtcNow + request.Timeout;
            JobRecord? job;
            while (true)
            {
                job = jobs.Get(request.JobId);
                if (job is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
                if (JobStateMachine.IsTerminal(job.Status)) break;
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                        "job.wait-timeout", FailureReason.Busy,
                        "Timed out after " + request.Timeout + " waiting for job '" + request.JobId +
                        "' to finish; it is still " + JobStatusJson.ToWire(job.Status) +
                        ". Check again with 'jobs show " + request.JobId + " --project <fwdata>'.",
                        Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(job.Status)))));
                }
                Thread.Sleep(WaitPollInterval);
            }

            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(job.JobId, job.ProjectKey, true,
                job.Kind, job.Status, job.Attempt, job.UpdatedUtc, job.CancellationRequested, job.FailureCategory,
                job.Version));
        });
    }

    /// <summary>Reports what the durable store currently says about one job.</summary>
    public static CommandOutcome<JobStatusResponse> Show(ShowJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            return CommandOutcome<JobStatusResponse>.Refused(JobIdRequired());

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var entry = new JobRepository(database).GetWithQueueOrder(request.JobId);
            if (entry is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));

            var job = entry.Value.Job;
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(job.JobId, job.ProjectKey, true,
                job.Kind, job.Status, job.Attempt, job.UpdatedUtc, job.CancellationRequested, job.FailureCategory,
                job.Version, entry.Value.QueueOrder));
        });
    }

    /// <summary>
    /// The Assessments one job produced — the lookup an agent needs when it holds a Trial's job id and
    /// nothing else, having enqueued several without waiting on any of them. Reads the ids a completed
    /// Trial recorded on its own outcome and looks each one up, rather than re-deriving them from the job's
    /// input.
    /// </summary>
    public static CommandOutcome<JobAssessmentsResponse> Assessments(JobAssessmentsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.JobId))
            return CommandOutcome<JobAssessmentsResponse>.Refused(JobIdRequired());

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var job = new JobRepository(database).Get(request.JobId);
            if (job is null) return CommandOutcome<JobAssessmentsResponse>.Refused(JobNotFound(request.JobId));

            var assessmentIds = TryReadAssessmentIds(job.ResultJson);
            if (assessmentIds is null)
            {
                return CommandOutcome<JobAssessmentsResponse>.Refused(new Refusal(
                    "job.no-assessments", FailureReason.Refused,
                    "Job '" + request.JobId + "' recorded no Assessments" +
                    (JobStateMachine.IsTerminal(job.Status)
                        ? "."
                        : "; it is still " + JobStatusJson.ToWire(job.Status) + "."),
                    Fact(("jobId", request.JobId))));
            }

            var repository = new AssessmentRepository(database);
            var summaries = assessmentIds
                .Select(repository.Get)
                .Select(record => new JobAssessmentSummary(record.AssessmentId, record.Assessor, record.Kind, record.SavedUtc))
                .ToArray();
            return CommandOutcome<JobAssessmentsResponse>.Success(
                new JobAssessmentsResponse(request.JobId, summaries));
        });
    }

    // Reads the "assessmentIds" array a completed Trial's own ResultJson carries; null for anything else.
    private static IReadOnlyList<string>? TryReadAssessmentIds(string? resultJson)
    {
        if (resultJson is null) return null;
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (!document.RootElement.TryGetProperty("assessmentIds", out var array) ||
                array.ValueKind != JsonValueKind.Array)
                return null;
            return array.EnumerateArray().Select(element => element.GetString()!).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Cancels one job. A job still queued (or parked waiting for a Baseline or the project host) is
    /// moved straight to <c>cancelled</c> — nothing is running it, so no runner is needed. A running job
    /// only has its cancellation flag set; the runner that holds it reads the flag on its own heartbeat
    /// and cancels the handler's token from there, landing in the same terminal state.
    /// </summary>
    public static CommandOutcome<JobStatusResponse> Cancel(CancelJobRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var current = jobs.Get(request.JobId);
            if (current is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (JobStateMachine.IsTerminal(current.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.already-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' already finished as " + JobStatusJson.ToWire(current.Status) +
                    "; there is nothing to cancel.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(current.Status)))));
            }

            var changed = current.Status == JobStatus.Running
                ? jobs.RequestCancellation(request.JobId, current.Version)
                : jobs.Transition(request.JobId, JobStatus.Cancelled, current.Version, JobFailureCategory.Cancellation);

            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(changed.JobId, changed.ProjectKey,
                true, changed.Kind, changed.Status, changed.Attempt, changed.UpdatedUtc,
                changed.CancellationRequested, changed.FailureCategory, changed.Version));
        });
    }

    /// <summary>Starts a fresh attempt of a terminal job's lineage, claimable exactly like a new job.</summary>
    public static CommandOutcome<JobStatusResponse> Requeue(RequeueJobRequest request)
    {
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var current = jobs.Get(request.JobId);
            if (current is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (!JobStateMachine.IsTerminal(current.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.not-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' is still " + JobStatusJson.ToWire(current.Status) +
                    "; only a finished job can be requeued.",
                    Fact(("jobId", request.JobId), ("status", JobStatusJson.ToWire(current.Status)))));
            }

            var retried = jobs.Retry(request.JobId, current.Version);
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(retried.JobId, retried.ProjectKey,
                true, retried.Kind, retried.Status, retried.Attempt, retried.UpdatedUtc,
                retried.CancellationRequested, retried.FailureCategory, retried.Version));
        });
    }

    /// <summary>Every active job across every Known project, in the order <see cref="JobClaims.Claim"/> takes it.</summary>
    public static CommandOutcome<JobQueueListResponse> ListAll(ListActiveJobsRequest request)
    {
        var entries = ReadGlobalActiveQueue(request.ProductVersion);
        return CommandOutcome<JobQueueListResponse>.Success(new JobQueueListResponse(entries.Select(entry =>
            new JobQueueEntryResponse(entry.Job.JobId, entry.Job.ProjectKey, entry.Project.FullFwDataPath,
                entry.Job.Kind, entry.Job.Status, entry.Job.Attempt, entry.Job.UpdatedUtc, entry.QueueOrder))
            .ToArray()));
    }

    /// <summary>
    /// Repositions one job in the single global queue by writing its own <c>QueueOrder</c> alone — one
    /// row, in this job's own project database, every time. A neighbour's row is never touched: it usually
    /// lives in a different project's database, and no transaction spans two SQLite files, so a write that
    /// depended on a neighbour's row too could half-complete and reorder a job nobody asked to move.
    /// </summary>
    /// <remarks>
    /// <c>--before</c>'s ordinary case is a midpoint between the named job and its global predecessor. A
    /// predecessor tied with it has no midpoint — but a tie means their relative order was already
    /// arbitrary (decided only by <c>JobId</c>, nothing the caller chose), so landing the mover just below
    /// the target, ahead of the whole tied run, satisfies "before target" without needing to touch the
    /// predecessor's row at all.
    /// </remarks>
    public static CommandOutcome<JobStatusResponse> Move(MoveJobRequest request)
    {
        if (request.Target.Kind == JobMoveKind.Before &&
            string.Equals(request.Target.BeforeJobId, request.JobId, StringComparison.Ordinal))
        {
            return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                "job.invalid-move", FailureReason.InvalidArgument, "A job cannot be moved before itself.",
                Fact(("jobId", request.JobId))));
        }

        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, project) =>
        {
            var jobs = new JobRepository(database);
            var mover = jobs.GetWithQueueOrder(request.JobId);
            if (mover is null) return CommandOutcome<JobStatusResponse>.Refused(JobNotFound(request.JobId));
            if (JobStateMachine.IsTerminal(mover.Value.Job.Status))
            {
                return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                    "job.already-finished", FailureReason.Refused,
                    "Job '" + request.JobId + "' already finished; a terminal job's position cannot be changed.",
                    Fact(("jobId", request.JobId))));
            }

            var others = ReadGlobalActiveQueue(request.ProductVersion)
                .Where(entry => !string.Equals(entry.Job.JobId, request.JobId, StringComparison.Ordinal)).ToArray();

            double newOrder;
            switch (request.Target.Kind)
            {
                case JobMoveKind.ToTop:
                    newOrder = others.Length == 0 ? mover.Value.QueueOrder : others[0].QueueOrder - 1.0;
                    break;
                case JobMoveKind.ToBottom:
                    newOrder = others.Length == 0 ? mover.Value.QueueOrder : others[^1].QueueOrder + 1.0;
                    break;
                default:
                    var targetIndex = Array.FindIndex(others,
                        entry => string.Equals(entry.Job.JobId, request.Target.BeforeJobId, StringComparison.Ordinal));
                    if (targetIndex < 0)
                    {
                        return CommandOutcome<JobStatusResponse>.Refused(new Refusal(
                            "job.move-target-not-found", FailureReason.NotFound,
                            "No active job '" + request.Target.BeforeJobId + "' is recorded to move before.",
                            Fact(("jobId", request.Target.BeforeJobId))));
                    }
                    newOrder = QueueOrderBefore(others, targetIndex);
                    break;
            }

            var moved = jobs.SetQueueOrder(request.JobId, newOrder, mover.Value.Job.Version, NowStamp());
            return CommandOutcome<JobStatusResponse>.Success(new JobStatusResponse(moved.JobId, moved.ProjectKey,
                true, moved.Kind, moved.Status, moved.Attempt, moved.UpdatedUtc, moved.CancellationRequested,
                moved.FailureCategory, moved.Version, newOrder));
        });
    }

    /// A tie has no midpoint; landing just below the target puts the mover ahead of the whole tied run.
    private static double QueueOrderBefore(IReadOnlyList<GlobalQueueEntry> others, int targetIndex)
    {
        var target = others[targetIndex];
        if (targetIndex == 0) return target.QueueOrder - 1.0;

        var predecessor = others[targetIndex - 1];
        return predecessor.QueueOrder < target.QueueOrder
            ? (predecessor.QueueOrder + target.QueueOrder) / 2.0
            : target.QueueOrder - 0.5;
    }

    /// Every active job in every reachable Known project, in global QueueOrder-then-JobId order.
    private static IReadOnlyList<GlobalQueueEntry> ReadGlobalActiveQueue(string productVersion)
    {
        using var machine = MachineDatabase.Open(RunnerOptions.ResolveRoot());
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, ParseProductVersion(productVersion));
        var entries = new List<GlobalQueueEntry>();
        foreach (var known in new KnownProjectRegistry(machine).List())
        {
            if (!File.Exists(known.FullFwDataPath)) continue;
            var locator = new ProjectLocator(known.FullFwDataPath, Path.GetFileNameWithoutExtension(known.FullFwDataPath));
            try
            {
                using var database = catalog.OpenOwned(locator);
                foreach (var entry in new JobRepository(database).ListActiveByQueueOrder(known.WorkspaceKey))
                    entries.Add(new GlobalQueueEntry(known, entry.Job, entry.QueueOrder));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
            {
                // Skipped, not thrown: one unreadable project must not hide every other project's queue.
            }
        }
        return entries.OrderBy(entry => entry.QueueOrder).ThenBy(entry => entry.Job.JobId, StringComparer.Ordinal)
            .ToArray();
    }

    private static Version ParseProductVersion(string productVersion) =>
        Version.TryParse(productVersion, out var parsed) ? parsed : new Version(1, 0);

    private static string NowStamp() =>
        DateTimeOffset.UtcNow.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

    private static Refusal JobNotFound(string jobId) =>
        new("job.not-found", FailureReason.NotFound,
            "No job '" + jobId + "' is recorded for this project.", Fact(("jobId", jobId)));

    private static Refusal JobIdRequired() =>
        new("job.invalid-id", FailureReason.InvalidArgument, "A job id is required.");

    private static Dictionary<string, string> Fact(params (string Key, string? Value)[] entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null) facts[key] = value;
        }
        return facts;
    }

    /// <summary>One active job read while assembling the cross-project queue view.</summary>
    private readonly record struct GlobalQueueEntry(KnownProjectRecord Project, JobRecord Job, double QueueOrder);

    // Reads a published Dry Run's JSON back into the model the renderer takes.
    private static DryRunModel ParsePublishedDryRun(string dryRunJson)
    {
        using var document = JsonDocument.Parse(dryRunJson);
        var root = document.RootElement;
        var anchor = JsonSerializer.Deserialize<BoundDryRunAnchor>(
            root.GetProperty("anchor").GetRawText(), MotifJson.CreateOptions())!;
        return new DryRunModel(
            root.GetProperty("intentDigest").GetString()!,
            root.GetProperty("baselineNote").GetString()!,
            ParseExpectedEffects(root.GetProperty("expectedEffects")),
            root.GetProperty("effectDigest").GetString()!,
            anchor);
    }

    private static IReadOnlyList<ExpectedEffect> ParseExpectedEffects(JsonElement array)
    {
        var effects = new List<ExpectedEffect>();
        foreach (var element in array.EnumerateArray())
        {
            effects.Add(new ExpectedEffect(
                CanonicalId.Parse(element.GetProperty("canonicalId").GetString()!),
                element.GetProperty("field").GetString()!,
                ReadAlternatives(element.GetProperty("before")),
                ReadAlternatives(element.GetProperty("after"))));
        }
        return effects;
    }

    private static IReadOnlyDictionary<string, string> ReadAlternatives(JsonElement alternatives)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in alternatives.EnumerateObject())
            map[property.Name] = property.Value.GetString() ?? "";
        return map;
    }
}

/// <summary>Which end of the global queue <c>jobs move</c> targets.</summary>
public enum JobMoveKind
{
    ToTop,
    ToBottom,
    Before
}

/// <summary>One <c>jobs move</c> invocation's destination: an end of the queue, or immediately before a named job.</summary>
public readonly record struct JobMoveTarget(JobMoveKind Kind, string? BeforeJobId)
{
    public static JobMoveTarget ToTop() => new(JobMoveKind.ToTop, null);
    public static JobMoveTarget ToBottom() => new(JobMoveKind.ToBottom, null);
    public static JobMoveTarget Before(string jobId) => new(JobMoveKind.Before, jobId);
}
