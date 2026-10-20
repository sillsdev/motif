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
using SIL.Motif.Host.Installation;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Runner.AppliedLog;
using SIL.Motif.Runner.DryRun;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker;

internal static class WorkerRuntime
{
    private const string BaselineRefreshKind = ProjectJobHandlers.BaselineRefreshKind;
    private const string DryRunKind = ProjectJobHandlers.DryRunKind;
    private const string TrialKind = TrialJobHandler.TrialKind;

    /// Matches the bound <see cref="JobRepository.RetryInfrastructure"/> itself applies to any lineage.
    private const int MaxAutomaticBaselineRefreshAttempts = 3;

    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(200);

    // Short against the idle poll: retirement waits out a sweep in progress, and must land between two.
    private static readonly TimeSpan RetireRetryInterval = TimeSpan.FromMilliseconds(20);

    // Only needs to outlast an exiting owner's own teardown (sub-second); the idle timeout is minutes.
    private static readonly TimeSpan OwnershipRetryWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan OwnershipRetryPoll = TimeSpan.FromMilliseconds(50);
    private static int _trialScratchSweepComplete;

    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return await RunWorkerAsync(args).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Escaped everything: reported and exited, the way the CLI treats an escaped exception.
            Console.Error.WriteLine("error: " + exception.Message);
            return FailureEnvelope.ExitCodeFor(FailureReason.StoreInconsistent);
        }
    }

    private static async Task<int> RunWorkerAsync(string[] args)
    {
        var options = RunnerOptions.Read(args);
        using var host = JobRunnerHost.ForRoot(options.Root, options.OwnerNamespace);
        var ownership = WorkspaceOwnership.Bootstrap(options.Root);
        host.ConfigureWorkspaces(ownership);
        var ownerId = "runner-" + Environment.ProcessId.ToString();
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
        var runtimes = host.CreateRuntimeRegistry(catalog,
            (jobs, key) =>
            {
                var artifacts = new EvidenceArtifactRepository(jobs.Database);
                return new WorkerRecoveryCoordinator(new WorkerRecovery(jobs, ownerId: ownerId),
                    new WorkspaceCleaner(ownership), new EvidenceRetentionCleaner(artifacts, ownership),
                    new EvidenceArtifactPublisher(options.Root, key));
            });
        if (!await TryAcquireOwnershipWithRetryAsync(host, wakeProjectPath: options.WakeProjectPath,
            extraWaitLimit: options.Lease, reportWarning: Console.Error.WriteLine)
            .ConfigureAwait(false))
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
        RunnerIdentity.Publish(options.Root);
        host.Start();

        using var machine = MachineDatabase.Open(options.Root);
        // One parser queue for the runner's life: every sweep tick reuses it rather than starting another.
        using var invoker = new PanGlossInvoker(options.ParserPath);
        var knownProjects = new KnownProjectRegistry(machine);
        var activity = new SweepActivity();
        var sweeping = SweepUntilCancelledAsync(knownProjects, runtimes, host.ProjectLanes, options, invoker,
            ownerId, activity, shutdown.Token);

        var lifetime = RunUntilRetiredAsync(new WorkerLifetime(), options.IdleTimeout, activity, shutdown.Token,
            MotifUpdateGate.IsWorkerRetirementRequested);
        // A sweep that fails, e.g. because the machine database went away, ends the runner now, not at idle.
        await Task.WhenAny(lifetime, sweeping).ConfigureAwait(false);
        shutdown.Cancel();
        await lifetime.ConfigureAwait(false);
        await sweeping.ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Retries the initial handover window, then admits one bounded waiter while the wake project has queued work.
    /// The queue stays in the database; the waiter mutex only limits competing successors to one.
    /// </summary>
    internal static async Task<bool> TryAcquireOwnershipWithRetryAsync(
        JobRunnerHost host, TimeProvider? timeProvider = null, string? wakeProjectPath = null,
        TimeSpan? extraWaitLimit = null, Action<string>? reportWarning = null,
        Func<string, bool>? queuedWorkProbe = null)
    {
        timeProvider ??= TimeProvider.System;
        var extraLimit = extraWaitLimit ?? TimeSpan.FromMinutes(5);
        if (extraLimit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(extraWaitLimit));
        var started = timeProvider.GetTimestamp();
        WorkerMutexOwner? waiter = null;
        try
        {
            while (true)
            {
                if (host.TryAcquireOwnership()) return true;
                if (timeProvider.GetElapsedTime(started) >= OwnershipRetryWindow)
                {
                    if (wakeProjectPath is null) return false;
                    if (waiter is null)
                    {
                        waiter = new WorkerMutexOwner(host.OwnerName + ".WakeWaiter");
                        if (!waiter.TryAcquire()) return false;
                    }
                    if (timeProvider.GetElapsedTime(started) >= OwnershipRetryWindow + extraLimit)
                    {
                        reportWarning?.Invoke("warning: runner wake gave up waiting for ownership while queued work " +
                            "remained for '" + wakeProjectPath + "' after " + extraLimit + ".");
                        return false;
                    }
                    if (!HasQueuedWakeWork(wakeProjectPath, queuedWorkProbe)) return false;
                }
                await Task.Delay(OwnershipRetryPoll, timeProvider).ConfigureAwait(false);
            }
        }
        finally { waiter?.Dispose(); }
    }

    private static bool HasQueuedWakeWork(string wakeProjectPath, Func<string, bool>? queuedWorkProbe)
    {
        try
        {
            if (queuedWorkProbe is not null) return queuedWorkProbe(wakeProjectPath);
            if (!File.Exists(wakeProjectPath)) return false;
            var project = new ProjectLocator(wakeProjectPath, Path.GetFileNameWithoutExtension(wakeProjectPath));
            var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
            using var database = catalog.OpenOwned(project, TimeSpan.FromMilliseconds(100));
            return new JobRepository(database).ListActive().Any(job => job.Status == JobStatus.Queued);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            MotifStoreLockException)
        {
            return true;
        }
    }

    /// <summary>
    /// Waits out the idle timeout, or an update's request to exit, then retires, unless a sweep found work in the
    /// meantime.
    /// </summary>
    internal static async Task RunUntilRetiredAsync(WorkerLifetime lifetime, TimeSpan idleTimeout,
        SweepActivity activity, CancellationToken shutdown, Func<bool>? retirementRequested = null)
    {
        do
        {
            using var idleWait = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
            var watching = WatchForRetirementRequestAsync(retirementRequested, idleWait);
            await lifetime.RunUntilIdleAsync(idleTimeout, () => activity.HasActiveWork, idleWait.Token)
                .ConfigureAwait(false);
            idleWait.Cancel();
            await watching.ConfigureAwait(false);
            if (shutdown.IsCancellationRequested || await RetireAsync(activity, shutdown).ConfigureAwait(false))
                return;
            // A request a busy runner cannot answer stays up; poll for the work to end instead of spinning.
            if (retirementRequested?.Invoke() == true)
            {
                try { await Task.Delay(IdlePollInterval, shutdown).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }
        while (true);
    }

    private static async Task WatchForRetirementRequestAsync(Func<bool>? retirementRequested,
        CancellationTokenSource idleWait)
    {
        if (retirementRequested is null) return;
        while (!idleWait.IsCancellationRequested)
        {
            if (retirementRequested())
            {
                idleWait.Cancel();
                return;
            }
            try { await Task.Delay(IdlePollInterval, idleWait.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    // False when a sweep found work, which restarts the idle timer; a sweep still scanning is only waited out.
    private static async Task<bool> RetireAsync(SweepActivity activity, CancellationToken shutdown)
    {
        while (!activity.TryRetire())
        {
            if (activity.HasActiveWork) return false;
            try { await Task.Delay(RetireRetryInterval, shutdown).ConfigureAwait(false); }
            catch (OperationCanceledException) { return true; }
        }
        return true;
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
            if (outcome.JobId is not null) continue;
            try { await Task.Delay(IdlePollInterval, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>The sweep's own record of whether anything, anywhere, is keeping the runner alive.</summary>
    /// <remarks>
    /// Retiring and sweeping are decided under one lock, so exactly one of them wins, and a runner retires
    /// only between sweeps. Retiring mid-sweep would stop the runner under a job it had just taken, pinned by
    /// `ARunnerThatHasRetiredClaimsNothingSoItsSuccessorCan`; or it would decline a claim after a slow scan,
    /// when the successor its enqueue kicked has already given up on the mutex, pinned by
    /// `ARunnerCannotRetireWhileASweepIsStillScanning`.
    /// </remarks>
    internal sealed class SweepActivity
    {
        private readonly object _gate = new();

        // Busy until the first sweep says otherwise: a slow first sweep must not let the idle timer expire unseen.
        private bool _hasActiveWork = true;
        private bool _sweeping;
        private bool _retired;

        public bool HasActiveWork
        {
            get { lock (_gate) return _hasActiveWork; }
        }

        public void Set(bool hasActiveWork)
        {
            lock (_gate) _hasActiveWork = hasActiveWork;
        }

        /// <summary>Commits the runner to exiting, unless a sweep is running or has found work.</summary>
        public bool TryRetire()
        {
            lock (_gate)
            {
                if (_hasActiveWork || _sweeping) return false;
                _retired = true;
                return true;
            }
        }

        /// <summary>Starts a sweep; <see langword="false"/> once the runner has retired.</summary>
        public bool TryBeginSweep()
        {
            lock (_gate)
            {
                if (_retired) return false;
                _sweeping = true;
                return true;
            }
        }

        /// <summary>Ends a sweep with what it found, which is what the idle timer then reads.</summary>
        public void EndSweep(bool hasActiveWork)
        {
            lock (_gate)
            {
                _sweeping = false;
                _hasActiveWork = hasActiveWork;
            }
        }
    }

    /// <summary>
    /// Opens every reachable Known project, reconciles its parked jobs, then claims and runs the
    /// single globally-first job across all of them by <c>QueueOrder</c> then <c>JobId</c>: a k-way merge
    /// over each project's own queue head, not a drain of one project before the next.
    /// </summary>
    /// <returns>
    /// The claimed job's id (or <c>null</c> when nothing across every Known project was claimable), paired
    /// with whether any Known project had queued, running, or waiting work at any point during this tick.
    /// </returns>
    /// <param name="activity">The shared activity state observed by the runner lifetime monitor.</param>
    /// <param name="runClaimedAsync">Runs a claimed job; the default dispatches through its project loop.</param>
    /// <param name="scannedAsync">Runs once every project is open and before any claim; a test holds it.</param>
    internal static async Task<SweepOutcome> SweepOnceAsync(KnownProjectRegistry knownProjects,
        Projects.ProjectRuntimeRegistry runtimes, Scheduling.ProjectLaneRegistry lanes, RunnerOptions options,
        IPanGlossInvoker invoker, string ownerId, CancellationToken cancellationToken,
        SweepActivity? activity = null,
        Func<JobRecord, CancellationToken, Task>? runClaimedAsync = null,
        Func<Task>? scannedAsync = null,
        Func<Projects.ProjectRuntime, IReadOnlyList<JobRecord>>? listActive = null,
        Action<string>? reportWarning = null)
    {
        activity ??= new SweepActivity();
        // A retired runner starts nothing: the row stays queued for the runner its enqueue kicked.
        if (!activity.TryBeginSweep()) return new SweepOutcome(null, false);
        var outcome = new SweepOutcome(null, true);
        try
        {
            outcome = await SweepCoreAsync(knownProjects, runtimes, lanes, options, invoker, ownerId,
                cancellationToken, activity, runClaimedAsync, scannedAsync, listActive,
                reportWarning ?? Console.Error.WriteLine).ConfigureAwait(false);
            return outcome;
        }
        finally
        {
            activity.EndSweep(outcome.HasActiveWork);
        }
    }

    private static async Task<SweepOutcome> SweepCoreAsync(KnownProjectRegistry knownProjects,
        Projects.ProjectRuntimeRegistry runtimes, Scheduling.ProjectLaneRegistry lanes, RunnerOptions options,
        IPanGlossInvoker invoker, string ownerId, CancellationToken cancellationToken, SweepActivity activity,
        Func<JobRecord, CancellationToken, Task>? runClaimedAsync, Func<Task>? scannedAsync,
        Func<Projects.ProjectRuntime, IReadOnlyList<JobRecord>>? listActive, Action<string> reportWarning)
    {
        var opened = new List<(Projects.ProjectRuntime Runtime, JobRunnerLoop Loop)>();
        var hasActiveWork = false;
        var allKnownProjectsOpened = true;
        foreach (var known in FromMachineDatabase(() => knownProjects.List()))
        {
            if (!File.Exists(known.FullFwDataPath))
            {
                FromMachineDatabase(() => { knownProjects.Forget(known.WorkspaceKey); return true; });
                continue;
            }

            try
            {
                var project = new ProjectLocator(known.FullFwDataPath,
                    Path.GetFileNameWithoutExtension(known.FullFwDataPath));
                var runtime = runtimes.GetOrOpen(project);
                ReconcileParkedJobs(runtime, DateTimeOffset.UtcNow, listActive);
                if ((listActive?.Invoke(runtime) ?? runtime.Jobs.ListActive(runtime.WorkspaceKey)).Count != 0)
                    hasActiveWork = true;
                else
                    runtime.Jobs.PurgeArchived(ArchivePolicy.Default);
                opened.Add((runtime, ProjectJobHandlers.CreateLoop(runtime.Database, runtime.Baselines,
                    runtime.WorkspaceKey, project, options, invoker, ownerId, lanes)));
            }
            catch (Exception exception)
            {
                allKnownProjectsOpened = false;
                hasActiveWork = true;
                ReportProjectSweepFailure(known.FullFwDataPath, "be swept", exception, reportWarning);
            }
        }

        if (allKnownProjectsOpened && Interlocked.CompareExchange(ref _trialScratchSweepComplete, 1, 0) == 0)
        {
            try
            {
                TrialScratchRetention.Sweep(opened.Select(entry =>
                    (entry.Runtime.Database, entry.Runtime.WorkspaceKey)));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                Microsoft.Data.Sqlite.SqliteException or InvalidDataException)
            {
                Volatile.Write(ref _trialScratchSweepComplete, 0);
                Console.Error.WriteLine("warning: Trial scratch cleanup could not read every known project (" +
                    exception.Message + "). It will be retried later.");
            }
        }

        if (scannedAsync is not null) await scannedAsync().ConfigureAwait(false);
        if (opened.Count == 0) return new SweepOutcome(null, hasActiveWork);

        var candidates = new List<(Projects.ProjectRuntime Runtime, JobRunnerLoop Loop, JobQueueHead Head)>();
        foreach (var candidate in opened)
        {
            try
            {
                if (candidate.Loop.PeekHead() is { } head)
                    candidates.Add((candidate.Runtime, candidate.Loop, head));
            }
            catch (Exception exception)
            {
                hasActiveWork = true;
                ReportProjectSweepFailure(candidate.Runtime.Project.FullFwDataPath,
                    "read its next job", exception, reportWarning);
            }
        }
        candidates.Sort((left, right) => IsEarlier(left.Head, right.Head) ? -1 :
            IsEarlier(right.Head, left.Head) ? 1 : 0);
        if (candidates.Count == 0) return new SweepOutcome(null, hasActiveWork);

        // A stopping runner leaves the row queued for the runner its enqueue kicked.
        if (cancellationToken.IsCancellationRequested) return new SweepOutcome(null, hasActiveWork);
        using var activityLease = MotifUpdateGate.TryAcquire();
        if (activityLease is null) return new SweepOutcome(null, hasActiveWork);

        foreach (var candidate in candidates)
        {
            JobRecord? claimed;
            try { claimed = candidate.Loop.TryClaim(); }
            catch (Exception exception)
            {
                hasActiveWork = true;
                ReportProjectSweepFailure(candidate.Runtime.Project.FullFwDataPath,
                    "claim its next job", exception, reportWarning);
                continue;
            }
            if (claimed is null) continue;
            activity.Set(true);
            if (runClaimedAsync is null)
                await candidate.Loop.RunClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
            else
                await runClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
            return new SweepOutcome(claimed.JobId, true);
        }
        return new SweepOutcome(null, hasActiveWork);
    }

    private static void ReportProjectSweepFailure(string path, string step, Exception exception,
        Action<string> reportWarning) => reportWarning("warning: Known project '" + path + "' could not " +
        step + " for this sweep (" + exception.Message + "). It will be retried next sweep.");

    /// <summary>
    /// Runs one read or write against the machine database, reporting any failure of it as the loss of that
    /// database, whatever SQLite said.
    /// </summary>
    /// <remarks>
    /// A root deleted under a live runner fails in more than one way: a missing directory, a missing file
    /// that the next connection recreates empty so the query finds no table, or a lock left behind. Each
    /// escapes the runner, which exits with the store failure code; this makes the report say which store
    /// was lost, pinned by `AMachineDatabaseFileDeletedBetweenSweepsFailsTheSweepAsAMachineDatabaseLoss`.
    /// </remarks>
    internal static T FromMachineDatabase<T>(Func<T> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException or
            InvalidDataException)
        {
            throw new IOException("The Motif machine database was lost or became unusable (" +
                exception.Message + ").", exception);
        }
    }

    /// <summary>One sweep tick's result: the job it ran, if any, and whether any project had active work.</summary>
    internal readonly record struct SweepOutcome(string? JobId, bool HasActiveWork);

    /// QueueOrder ties are real, so JobId is what makes the order stable.
    private static bool IsEarlier(JobQueueHead candidate, JobQueueHead current) =>
        candidate.QueueOrder != current.QueueOrder
            ? candidate.QueueOrder < current.QueueOrder
            : string.CompareOrdinal(candidate.JobId, current.JobId) < 0;

    /// <summary>
    /// Closes the two ways a parked Dry Run or Trial could otherwise wait forever: once a Baseline exists every
    /// parked row for this project is made claimable again directly; until then, at most one
    /// <c>baseline-refresh</c> is kept in flight, and once that lineage exhausts its bounded attempts
    /// every row still waiting on it fails rather than staying parked with nothing left to wait for.
    /// </summary>
    /// <remarks>
    /// A sweep reconciles only between claims, so no parked row it sees is still in flight in this runner:
    /// one is either waiting for a Baseline or was left parked by a runner that stopped. Either way it is
    /// active, and an active row nothing moves keeps every later runner from idling out. That is also why a
    /// row parked at <c>waiting-for-project-host</c> is queued again outright, pinned by
    /// `AProjectHostWaitLeftByAStoppedRunnerIsQueuedAgain`.
    /// </remarks>
    internal static void ReconcileParkedJobs(Projects.ProjectRuntime runtime, DateTimeOffset now,
        Func<Projects.ProjectRuntime, IReadOnlyList<JobRecord>>? listActive = null)
    {
        var active = listActive?.Invoke(runtime) ?? runtime.Jobs.ListActive(runtime.WorkspaceKey);
        foreach (var job in active.Where(job => job.Status == JobStatus.WaitingForProjectHost))
            runtime.Jobs.Transition(job.JobId, JobStatus.Queued, job.Version);
        var parked = active
            .Where(job => job.Status == JobStatus.WaitingForBaseline && job.Kind is DryRunKind or TrialKind)
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
        if (latest is null || latest.Status == JobStatus.Completed)
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
            detail = "The Baseline this job needs could not be produced after repeated attempts: " + reason
        });
        foreach (var job in parked)
            runtime.Jobs.Transition(job.JobId, JobStatus.Failed, job.Version, JobFailureCategory.Infrastructure,
                detail);
    }

    private static void EnqueueBaselineRefresh(Projects.ProjectRuntime runtime, DateTimeOffset now) =>
        runtime.Jobs.Create(CanonicalId.Mint("job/").Value, runtime.WorkspaceKey, BaselineRefreshKind, "{}",
            JobTimestamp.FormatUtc(now));
}
