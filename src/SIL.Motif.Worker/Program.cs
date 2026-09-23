using SIL.Motif.Host;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Config;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker;

internal static class Program
{
    private const string BaselineRefreshKind = "baseline-refresh";
    private const string DryRunKind = "dry-run";

    /// Matches the bound <see cref="JobRepository.RetryInfrastructure"/> itself applies to any lineage.
    private const int MaxAutomaticBaselineRefreshAttempts = 3;

    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(200);

    // Only needs to outlast an exiting owner's own teardown (sub-second); the idle timeout is minutes.
    private static readonly TimeSpan OwnershipRetryWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OwnershipRetryPoll = TimeSpan.FromMilliseconds(50);

    private static async Task<int> Main(string[] args)
    {
        CrashDialogs.Suppress();
        try
        {
            return await RunAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Escaped everything: reported and exited, the way the CLI treats an escaped exception.
            Console.Error.WriteLine("error: " + exception.Message);
            return FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent);
        }
    }

    private static async Task<int> RunAsync(string[] args)
    {
        var options = RunnerOptions.Read(args);
        using var host = options.OwnerNamespace is { } isolated
            ? JobRunnerHost.ForNamespace(isolated)
            : new JobRunnerHost();
        var ownership = WorkspaceOwnership.Bootstrap(options.Root);
        host.ConfigureWorkspaces(ownership);
        var ownerId = "runner-" + Environment.ProcessId.ToString();
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
        var runtimes = host.CreateRuntimeRegistry(catalog,
            (jobs, key) => new WorkerRecoveryCoordinator(
                new WorkerRecovery(jobs, ownerId: ownerId), new WorkspaceCleaner(ownership)));
        if (!await TryAcquireOwnershipWithRetryAsync(host).ConfigureAwait(false))
        {
            Console.WriteLine("existing runner: " + host.OwnerName);
            return 0;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };
        Console.WriteLine(host.OwnerName);
        host.Start();

        using var machine = MachineDatabase.Open(options.Root);
        // One parser queue for the runner's life: every sweep tick reuses it rather than starting another.
        using var invoker = new PanGlossInvoker();
        var knownProjects = new KnownProjectRegistry(machine);
        var activity = new SweepActivity();
        var sweeping = SweepUntilCancelledAsync(knownProjects, runtimes, host.ProjectLanes, options, invoker,
            ownerId, activity, shutdown.Token);

        var lifetime = new WorkerLifetime().RunUntilIdleAsync(options.IdleTimeout, () => activity.HasActiveWork,
            shutdown.Token);
        // A sweep that fails, e.g. because the machine database went away, ends the runner now, not at idle.
        await Task.WhenAny(lifetime, sweeping).ConfigureAwait(false);
        shutdown.Cancel();
        await lifetime.ConfigureAwait(false);
        await sweeping.ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Retries a bounded window rather than giving up on the first failed attempt: closes the race where
    /// the currently-owning runner is inside its final idle tick and about to release the mutex, not gone.
    /// </summary>
    internal static async Task<bool> TryAcquireOwnershipWithRetryAsync(JobRunnerHost host)
    {
        var deadline = DateTimeOffset.UtcNow + OwnershipRetryWindow;
        while (true)
        {
            if (host.TryAcquireOwnership()) return true;
            if (DateTimeOffset.UtcNow >= deadline) return false;
            await Task.Delay(OwnershipRetryPoll).ConfigureAwait(false);
        }
    }

    /// Ticks the sweep back-to-back while jobs keep being found; idles for <see cref="IdlePollInterval"/> when not.
    private static async Task SweepUntilCancelledAsync(KnownProjectRegistry knownProjects,
        Projects.ProjectRuntimeRegistry runtimes, Scheduling.ProjectLaneRegistry lanes, RunnerOptions options,
        IPanGlossInvoker invoker, string ownerId, SweepActivity activity, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SweepOutcome outcome;
            try
            {
                outcome = await SweepOnceAsync(knownProjects, runtimes, lanes, options, invoker, ownerId,
                    cancellationToken, activity).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            activity.Set(outcome.HasActiveWork);
            if (outcome.JobId is not null) continue;
            try { await Task.Delay(IdlePollInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The sweep's own record of whether anything, anywhere, is keeping the runner alive.</summary>
    internal sealed class SweepActivity
    {
        // Busy until the first sweep says otherwise: a slow first sweep must not let the idle timer expire unseen.
        private volatile bool _hasActiveWork = true;

        public bool HasActiveWork => _hasActiveWork;

        public void Set(bool hasActiveWork) => _hasActiveWork = hasActiveWork;
    }

    /// <summary>
    /// Opens every reachable Known project, reconciles its parked Dry Runs, then claims and runs the
    /// single globally-first job across all of them by <c>QueueOrder</c> then <c>JobId</c>: a k-way merge
    /// over each project's own queue head, not a drain of one project before the next.
    /// </summary>
    /// <returns>
    /// The claimed job's id (or <c>null</c> when nothing across every Known project was claimable), paired
    /// with whether any Known project had queued, running, or waiting work at any point during this tick.
    /// </returns>
    /// <param name="activity">The shared activity state observed by the runner lifetime monitor.</param>
    /// <param name="runClaimedAsync">Runs a claimed job; the default dispatches through its project loop.</param>
    internal static async Task<SweepOutcome> SweepOnceAsync(KnownProjectRegistry knownProjects,
        Projects.ProjectRuntimeRegistry runtimes, Scheduling.ProjectLaneRegistry lanes, RunnerOptions options,
        IPanGlossInvoker invoker, string ownerId, CancellationToken cancellationToken,
        SweepActivity? activity = null,
        Func<JobRecord, CancellationToken, Task>? runClaimedAsync = null)
    {
        activity ??= new SweepActivity();
        var opened = new List<(Projects.ProjectRuntime Runtime, JobRunnerLoop Loop)>();
        var hasActiveWork = false;
        foreach (var known in knownProjects.List())
        {
            if (!File.Exists(known.FullFwDataPath))
            {
                knownProjects.Forget(known.WorkspaceKey);
                continue;
            }

            Projects.ProjectRuntime runtime;
            ProjectLocator project;
            try
            {
                project = new ProjectLocator(known.FullFwDataPath,
                    Path.GetFileNameWithoutExtension(known.FullFwDataPath));
                runtime = runtimes.GetOrOpen(project);
            }
            catch (Exception exception)
            {
                // Not forgotten: an operator watching this log must still see the project, not lose it silently.
                Console.Error.WriteLine("warning: Known project '" + known.FullFwDataPath +
                    "' could not be opened for sweeping (" + exception.Message + "). It will be retried later.");
                continue;
            }

            try
            {
                ReconcileParkedDryRuns(runtime, DateTimeOffset.UtcNow);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine("warning: parked Dry Run reconciliation failed for '" +
                    known.FullFwDataPath + "' (" + exception.Message + ").");
            }
            if (runtime.Jobs.ListActive(runtime.WorkspaceKey).Count != 0) hasActiveWork = true;
            else runtime.Jobs.PurgeArchived(ArchivePolicy.Default);

            opened.Add((runtime, BuildLoop(runtime, project, options, invoker, ownerId, lanes)));
        }

        if (opened.Count == 0) return new SweepOutcome(null, hasActiveWork);

        (Projects.ProjectRuntime Runtime, JobRunnerLoop Loop)? chosen = null;
        var chosenHead = default(JobQueueHead);
        foreach (var candidate in opened)
        {
            if (candidate.Loop.PeekHead() is not { } head) continue;
            if (chosen is null || IsEarlier(head, chosenHead))
            {
                chosen = candidate;
                chosenHead = head;
            }
        }
        if (chosen is not { } winner) return new SweepOutcome(null, hasActiveWork);

        var claimed = winner.Loop.TryClaim();
        if (claimed is null) return new SweepOutcome(null, hasActiveWork);
        activity.Set(true);
        if (runClaimedAsync is null)
            await winner.Loop.RunClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
        else
            await runClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
        return new SweepOutcome(claimed.JobId, true);
    }

    /// <summary>One sweep tick's result: the job it ran, if any, and whether any project had active work.</summary>
    internal readonly record struct SweepOutcome(string? JobId, bool HasActiveWork);

    /// QueueOrder ties are real, so JobId is what makes the order stable.
    private static bool IsEarlier(JobQueueHead candidate, JobQueueHead current) =>
        candidate.QueueOrder != current.QueueOrder
            ? candidate.QueueOrder < current.QueueOrder
            : string.CompareOrdinal(candidate.JobId, current.JobId) < 0;

    /// <summary>
    /// Closes the two ways a parked Dry Run could otherwise wait forever: once a Baseline exists every
    /// parked row for this project is made claimable again directly; until then, at most one
    /// <c>baseline-refresh</c> is kept in flight, and once that lineage exhausts its bounded attempts
    /// every row still waiting on it fails rather than staying parked with nothing left to wait for.
    /// </summary>
    internal static void ReconcileParkedDryRuns(Projects.ProjectRuntime runtime, DateTimeOffset now)
    {
        var parked = runtime.Jobs.ListActive(runtime.WorkspaceKey)
            .Where(job => job.Status == JobStatus.WaitingForBaseline && job.Kind == DryRunKind)
            .ToArray();
        if (parked.Length == 0) return;

        if (runtime.Baselines.GetCurrent(runtime.WorkspaceKey) is not null)
        {
            foreach (var job in parked)
                runtime.Jobs.Transition(job.JobId, JobStatus.Queued, job.Version);
            return;
        }

        var refreshes = runtime.Jobs.ListByProjectAndKind(runtime.WorkspaceKey, BaselineRefreshKind);
        if (refreshes.Any(job => !JobStateMachine.IsTerminal(job.Status)))
            return;

        var latest = refreshes.Count == 0
            ? null
            : refreshes.Aggregate((left, right) =>
                string.CompareOrdinal(left.CreatedUtc, right.CreatedUtc) >= 0 ? left : right);
        if (latest is null)
        {
            EnqueueBaselineRefresh(runtime, now);
            return;
        }

        if (latest.FailureCategory == JobFailureCategory.Infrastructure &&
            latest.Attempt < MaxAutomaticBaselineRefreshAttempts)
        {
            try
            {
                runtime.Jobs.RetryInfrastructure(latest.JobId, latest.Version, now);
                return;
            }
            catch (InvalidOperationException)
            {
                // The lineage stopped being eligible between the check above and this attempt; fail below.
            }
        }

        var reason = latest.ResultJson ?? "the Baseline refresh did not succeed.";
        var detail = JsonSerializer.Serialize(new
        {
            detail = "The Baseline this Dry Run needs could not be produced after repeated attempts: " + reason
        });
        foreach (var job in parked)
            runtime.Jobs.Transition(job.JobId, JobStatus.Failed, job.Version, JobFailureCategory.Infrastructure,
                detail);
    }

    private static void EnqueueBaselineRefresh(Projects.ProjectRuntime runtime, DateTimeOffset now) =>
        runtime.Jobs.Create(CanonicalId.Mint("job/").Value, runtime.WorkspaceKey, BaselineRefreshKind, "{}",
            now.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

    /// <summary>Builds the handlers one project's claimed jobs are dispatched to.</summary>
    private static JobRunnerLoop BuildLoop(Projects.ProjectRuntime runtime, ProjectLocator project,
        RunnerOptions options, IPanGlossInvoker invoker, string ownerId, Scheduling.ProjectLaneRegistry lanes)
    {
        var publish = new BaselineRefresh(runtime.Baselines, options.Root);
        var refresh = new BaselineRefreshJobHandler(
            new BaselineRefreshBarrier(locator => new FwDataProjectLoader().LoadCache(locator.FullFwDataPath)),
            (cache, token) => publish.RefreshAsync(cache, project, token));
        var proposals = new ProposalRepository(runtime.Database);
        var dryRun = new DryRunJobHandler(runtime.Baselines, proposals, lanes, _ => null,
            (fwDataPath, _) =>
            {
                // One open of the published Baseline: peeked here for the applied log, consumed later to run.
                var scratch = new BaselineScratchFactory().OpenSingleUse(fwDataPath);
                var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)));

        var handlers = new Dictionary<string, JobRunnerLoop.Handler>(StringComparer.Ordinal)
        {
            [BaselineRefreshKind] = (_, token) => refresh.RunAsync(project, token),
            [DryRunKind] = (job, token) => dryRun.RunAsync(job, project, token),
        };

        if (TryBuildTrialHandler(runtime, proposals, lanes, options, invoker) is { } trial)
            handlers[TrialJobHandler.TrialKind] = (job, token) => trial.RunAsync(job, project, token);

        return new JobRunnerLoop(new JobClaims(runtime.Database), runtime.WorkspaceKey, ownerId: ownerId,
            lease: options.Lease, poll: TimeSpan.Zero, handlers: handlers);
    }

    /// <summary>Builds a Trial handler, or null when the parser or root is not usable.</summary>
    private static TrialJobHandler? TryBuildTrialHandler(Projects.ProjectRuntime runtime,
        ProposalRepository proposals, Scheduling.ProjectLaneRegistry lanes, RunnerOptions options,
        IPanGlossInvoker invoker)
    {
        // No executable, no Trial handler: a Trial job would otherwise fail on every attempt.
        if (PanGlossExecutable.TryLocate() is null) return null;
        IAssessorCatalog catalog;
        try
        {
            var ownership = WorkspaceOwnership.Bootstrap(options.Root);
            catalog = new AssessorCatalog(new IAssessor[]
            {
                new PanGlossAssessor(new StatsCacheStore(ownership), invoker),
            });
        }
        catch (ArgumentException)
        {
            return null;
        }

        var loader = new FwDataProjectLoader();
        return new TrialJobHandler(runtime.Baselines, proposals, lanes,
            new ProjectConfigurationReader(), catalog, new AssessmentRepository(runtime.Database),
            (fwDataPath, scratchRoot, _) =>
            {
                var factory = new ScratchCacheFactory(loader);
                var cache = factory.CreateFromFileCopy(fwDataPath, scratchRoot);
                var scratch = DryRunScratch.Adopt(cache, $"file copy under {scratchRoot}");
                var appliedProposalIds = ProjectAppliedLog.ReadAll(scratch.PeekCache())
                    .Select(entry => entry.ProposalId).ToArray();
                return Task.FromResult<(IReadOnlyCollection<Guid>, DryRunScratch?)>((appliedProposalIds, scratch));
            },
            (scratch, plan, _) => Task.FromResult(ProposalDryRunner.Run(scratch!, plan)),
            (cache, _) =>
            {
                if (cache is null)
                {
                    throw new InvalidOperationException(
                        "A Trial requires a real scratch to prepare a candidate for Assessment.");
                }
                loader.Save(cache);
                var directory = Path.GetDirectoryName(Path.GetFullPath(cache.ProjectId.Path))!;
                return Task.FromResult(directory);
            });
    }
}
