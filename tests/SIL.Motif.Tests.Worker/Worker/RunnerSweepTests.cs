using System.Collections.Concurrent;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Covers <see cref="SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync"/>: the runner reading every Known project
/// each tick and claiming the globally first job across all of them, rather than draining one project
/// before looking at the next.
/// </summary>
public sealed class RunnerSweepTests : IDisposable
{
    private const string OwnerId = "sweep-test-runner";
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-sweep-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectRuntimeRegistry _runtimes;
    private readonly ProjectLaneRegistry _lanes =
        new(_ => throw new InvalidOperationException("No Dry Run handler runs in this suite."));
    private readonly RunnerOptions _options;

    public RunnerSweepTests()
    {
        Directory.CreateDirectory(_root);
        _options = new RunnerOptions { Root = _root };
        var ownership = WorkspaceOwnership.Bootstrap(Path.Combine(_root, "owned"));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0));
        _runtimes = new ProjectRuntimeRegistry(catalog,
            (jobs, key) => new WorkerRecoveryCoordinator(new WorkerRecovery(jobs), new WorkspaceCleaner(ownership)),
            new ProjectRuntimeActivity());
    }

    [Fact]
    public async Task TheSweepClaimsAcrossProjectsInGlobalQueueOrderRatherThanProjectByProject()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var a = SeedProject(known, "project-a");
        var b = SeedProject(known, "project-b");

        // Interleaved: a1, then b1, then a2 — project b's single job sorts between project a's two.
        SeedJob(a, "a1", queueOrder: 1.0);
        SeedJob(b, "b1", queueOrder: 2.0);
        SeedJob(a, "a2", queueOrder: 3.0);

        var order = await DrainAsync(known);

        Assert.Equal(new[] { "a1", "b1", "a2" }, order);
    }

    [Fact]
    public async Task AClaimedJobKeepsTheRunnerAliveWhileTheJobIsRunning()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var runtime = SeedProject(known, "long-job");
        SeedJob(runtime, "slow-job", queueOrder: 1.0);

        var activity = new SIL.Motif.Worker.WorkerRuntime.SweepActivity();
        // An earlier sweep found nothing: the idle runner the initial busy state no longer covers.
        activity.Set(false);
        using var shutdown = new CancellationTokenSource();
        using var clock = new ManualWorkerClock();
        var idleTimeout = TimeSpan.FromSeconds(1);
        var lifetime = new WorkerLifetime(clock).RunUntilIdleAsync(
            idleTimeout, () => activity.HasActiveWork, shutdown.Token);
        await clock.WaitForDelayRequestAsync();

        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var sweeping = SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, CancellationToken.None, activity, async (_, _) =>
            {
                started.TrySetResult(true);
                await release.Task;
            });

        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.AdvanceNextDelay();

            var nextDelayRequested = clock.WaitForDelayRequestAsync();
            var observed = await Task.WhenAny(lifetime, nextDelayRequested);

            Assert.Same(nextDelayRequested, observed);
            Assert.False(lifetime.IsCompleted);
        }
        finally
        {
            release.TrySetResult(true);
            shutdown.Cancel();
            await sweeping;
            await lifetime;
        }
    }

    [Fact]
    public async Task ARunnerThatHasRetiredClaimsNothingSoItsSuccessorCan()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var runtime = SeedProject(known, "retired");
        SeedJob(runtime, "late-job", queueOrder: 1.0);
        var activity = new SIL.Motif.Worker.WorkerRuntime.SweepActivity();
        activity.Set(false);
        Assert.True(activity.TryRetire());
        var ran = false;

        var outcome = await SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, CancellationToken.None, activity,
            (_, _) => { ran = true; return Task.CompletedTask; });

        Assert.Null(outcome.JobId);
        Assert.False(ran);
        // Queued, not cancelled: the runner its enqueue kicked takes it once this one lets go of the mutex.
        Assert.Equal(JobStatus.Queued, runtime.Jobs.Get("late-job")!.Status);
    }

    [Fact]
    public async Task ARunnerCannotRetireWhileASweepIsStillScanning()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var runtime = SeedProject(known, "slow-scan");
        SeedJob(runtime, "late-job", queueOrder: 1.0);
        var activity = new SIL.Motif.Worker.WorkerRuntime.SweepActivity();
        activity.Set(false);
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ran = false;

        var sweeping = SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, CancellationToken.None, activity,
            (_, _) => { ran = true; return Task.CompletedTask; },
            async () => { scanned.TrySetResult(); await release.Task; });
        bool retiredMidScan;
        try
        {
            await scanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            retiredMidScan = activity.TryRetire();
        }
        finally
        {
            release.TrySetResult();
        }
        var outcome = await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        // Retiring here would decline the claim after a successor gave up waiting for the mutex.
        Assert.False(retiredMidScan);
        Assert.Equal("late-job", outcome.JobId);
        Assert.True(ran);
    }

    [Fact]
    public async Task AnIdleTimeoutThatExpiresMidScanRetiresOnlyOnceTheScanEnds()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        SeedProject(known, "idle-scan");
        var activity = new SIL.Motif.Worker.WorkerRuntime.SweepActivity();
        activity.Set(false);
        var scanned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shutdown = new CancellationTokenSource();

        var sweeping = SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, CancellationToken.None, activity,
            scannedAsync: async () => { scanned.TrySetResult(); await release.Task; });
        Task retiring;
        try
        {
            await scanned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            retiring = SIL.Motif.Worker.WorkerRuntime.RunUntilRetiredAsync(new WorkerLifetime(),
                TimeSpan.FromMilliseconds(50), activity, shutdown.Token);

            // Many idle timeouts pass while the scan is held; none of them may retire the runner.
            Assert.NotSame(retiring, await Task.WhenAny(retiring, Task.Delay(TimeSpan.FromMilliseconds(500))));
        }
        finally
        {
            release.TrySetResult();
        }
        await sweeping.WaitAsync(TimeSpan.FromSeconds(5));

        await retiring.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(activity.TryBeginSweep());
    }

    [Fact]
    public async Task AMachineDatabaseFileDeletedBetweenSweepsFailsTheSweepAsAMachineDatabaseLoss()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        SeedProject(known, "file-lost");
        // Unpooled connections are all closed between sweeps, so the file deletes as a user's root would.
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            File.Delete(machine.FullPath + suffix);

        var failure = await Assert.ThrowsAsync<IOException>(() => SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known,
            _runtimes, _lanes, _options, new FakeInvoker(), OwnerId, CancellationToken.None));

        Assert.Contains("machine database", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AMachineDatabaseMissingItsKnownProjectsTableFailsTheSweepAsAMachineDatabaseLoss()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        using (var connection = machine.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DROP TABLE KnownProjects;";
            command.ExecuteNonQuery();
        }

        var failure = await Assert.ThrowsAsync<IOException>(() => SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known,
            _runtimes, _lanes, _options, new FakeInvoker(), OwnerId, CancellationToken.None));

        Assert.Contains("machine database", failure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASweepThatBeginsAfterShutdownClaimsNothing()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var runtime = SeedProject(known, "shut-down");
        SeedJob(runtime, "late-job", queueOrder: 1.0);
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        var outcome = await SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, shutdown.Token, runClaimedAsync: (_, _) => Task.CompletedTask);

        Assert.Null(outcome.JobId);
        Assert.Equal(JobStatus.Queued, runtime.Jobs.Get("late-job")!.Status);
    }

    [Fact]
    public async Task ARunnerCannotRetireOnceASweepHasBegunToClaim()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var runtime = SeedProject(known, "claiming");
        SeedJob(runtime, "claimed-job", queueOrder: 1.0);
        var activity = new SIL.Motif.Worker.WorkerRuntime.SweepActivity();
        activity.Set(false);
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var sweeping = SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
            new FakeInvoker(), OwnerId, CancellationToken.None, activity, async (_, _) =>
            {
                started.TrySetResult(true);
                await release.Task;
            });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(activity.TryRetire());
        }
        finally
        {
            release.TrySetResult(true);
            await sweeping;
        }
    }

    [Fact]
    public async Task AKnownProjectWhoseFwdataWasDeletedIsForgottenAndDoesNotBreakTheSweep()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var missingPath = Path.Combine(_root, "missing.fwdata");
        known.Record(FakeWorkspaceKey(missingPath), missingPath, DateTimeOffset.UtcNow);
        var present = SeedProject(known, "still-here");
        SeedJob(present, "job-1", queueOrder: 1.0);

        var order = await DrainAsync(known);

        Assert.Equal(new[] { "job-1" }, order);
        Assert.DoesNotContain(known.List(), record => record.FullFwDataPath == missingPath);
    }

    [Fact]
    public async Task AKnownProjectWhosePairedDatabaseCannotBeOpenedIsSkippedRatherThanForgotten()
    {
        using var machine = MachineDatabase.Open(_options.Root);
        var known = new KnownProjectRegistry(machine);
        var corruptPath = Path.Combine(_root, "corrupt.fwdata");
        File.WriteAllText(corruptPath, "placeholder");
        var corruptDatabasePath = ProjectDatabaseCatalog.DatabasePathFor(
            new ProjectLocator(corruptPath, Path.GetFileNameWithoutExtension(corruptPath)));
        File.WriteAllBytes(corruptDatabasePath, new byte[] { 1, 2, 3, 4 });
        var corruptKey = FakeWorkspaceKey(corruptPath);
        known.Record(corruptKey, corruptPath, DateTimeOffset.UtcNow);

        var healthy = SeedProject(known, "healthy");
        SeedJob(healthy, "job-1", queueOrder: 1.0);

        var order = await DrainAsync(known);

        Assert.Equal(new[] { "job-1" }, order);
        // Logged, not forgotten: a corrupt store silently vanishing would leave queued work with no explanation.
        Assert.Contains(known.List(), record => record.WorkspaceKey == corruptKey);
    }

    private ProjectRuntime SeedProject(KnownProjectRegistry known, string identity)
    {
        var path = Path.Combine(_root, identity + ".fwdata");
        File.WriteAllText(path, "placeholder");
        var project = new ProjectLocator(path, identity);
        known.Record(ProjectWorkspaceKey.Compute(project), path, DateTimeOffset.UtcNow);
        return _runtimes.GetOrOpen(project);
    }

    private static void SeedJob(ProjectRuntime runtime, string jobId, double queueOrder)
    {
        runtime.Jobs.Create(jobId, runtime.WorkspaceKey, "probe", "{}",
            JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
        using var connection = runtime.Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Jobs SET QueueOrder = $order WHERE JobId = $id;";
        command.Parameters.AddWithValue("$order", queueOrder);
        command.Parameters.AddWithValue("$id", jobId);
        command.ExecuteNonQuery();
    }

    private static string FakeWorkspaceKey(string fwDataPath) =>
        ProjectWorkspaceKey.Compute(new ProjectLocator(fwDataPath, Path.GetFileNameWithoutExtension(fwDataPath)));

    private sealed class ManualWorkerClock : IWorkerClock, IDisposable
    {
        private readonly ConcurrentQueue<(TimeSpan Delay, TaskCompletionSource<bool> Completion)> _waiters = new();
        private readonly SemaphoreSlim _delayRequests = new(0);
        private long _ticks;

        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch + MonotonicNow;

        public TimeSpan MonotonicNow => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _waiters.Enqueue((delay, completion));
            _delayRequests.Release();
            await completion.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForDelayRequestAsync() => _delayRequests.WaitAsync();

        public void AdvanceNextDelay()
        {
            if (!_waiters.TryDequeue(out var waiter))
                throw new InvalidOperationException("No worker delay is waiting to advance.");
            Interlocked.Add(ref _ticks, waiter.Delay.Ticks);
            waiter.Completion.TrySetResult(true);
        }

        public void Dispose() => _delayRequests.Dispose();
    }

    /// Repeatedly ticks the sweep — exactly what the runner's own loop does — until nothing is claimable.
    private async Task<IReadOnlyList<string>> DrainAsync(KnownProjectRegistry known)
    {
        var invoker = new FakeInvoker();
        var claimed = new List<string>();
        var guard = 0;
        while (true)
        {
            var outcome = await SIL.Motif.Worker.WorkerRuntime.SweepOnceAsync(known, _runtimes, _lanes, _options,
                invoker, OwnerId, CancellationToken.None);
            if (outcome.JobId is not { } next) break;
            claimed.Add(next);
            if (++guard > 50) throw new InvalidOperationException("The sweep did not converge.");
        }
        return claimed;
    }

    public void Dispose()
    {
        _runtimes.Dispose();
        _lanes.Dispose();
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
