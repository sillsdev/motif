using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Covers <see cref="WorkerRuntime.TryAcquireOwnershipWithRetryAsync"/>: the reason a runner the CLI kicks does
/// not need a protocol to defer to one already alive, and the reason it does not give up the instant a
/// live owner is mid-shutdown rather than gone.
/// </summary>
public sealed class JobRunnerHostOwnershipTests
{
    [Fact]
    public void CurrentOwnerNameUsesThePlatformUserNamespace()
    {
        var name = JobRunnerHost.GetOwnerMutexName();

        if (OperatingSystem.IsWindows())
            Assert.StartsWith(@"Local\SIL.Motif.Worker.Owner.", name, StringComparison.Ordinal);
        else
        {
            Assert.Matches("^uid-[0-9]+$", WorkerIdentity.GetCurrentUserNamespace());
            Assert.EndsWith(WorkerIdentity.GetCurrentUserNamespace(), name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task KickingWhenARunnerIsAlreadyAliveDoesNotStartASecondOwner()
    {
        var ns = "kick-alive-" + Guid.NewGuid().ToString("N");
        using var alive = JobRunnerHost.ForNamespace(ns);
        Assert.True(alive.TryAcquireOwnership());

        using var kicked = JobRunnerHost.ForNamespace(ns);
        var acquired = await SIL.Motif.Worker.WorkerRuntime.TryAcquireOwnershipWithRetryAsync(kicked);

        Assert.False(acquired);
        Assert.False(kicked.IsOwner);
        Assert.True(alive.IsOwner);
    }

    [Fact]
    public async Task RetryingAcquiresOwnershipOnceTheExitingRunnerReleasesItWithinTheWindow()
    {
        var ns = "kick-race-" + Guid.NewGuid().ToString("N");
        using var exiting = JobRunnerHost.ForNamespace(ns);
        Assert.True(exiting.TryAcquireOwnership());

        using var kicked = JobRunnerHost.ForNamespace(ns);
        var clock = new RetryTimeProvider(DateTimeOffset.UtcNow);
        var retrying = SIL.Motif.Worker.WorkerRuntime.TryAcquireOwnershipWithRetryAsync(kicked, clock);

        await clock.WaitForTimerAsync();
        Assert.True(exiting.IsOwner);
        Assert.False(kicked.IsOwner);
        exiting.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await retrying.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(kicked.IsOwner);
    }

    [Fact]
    public async Task ALateQueuedJobKeepsTheKickedRunnerWaitingForTheRetiringOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-retiring-owner-" + Guid.NewGuid().ToString("N"));
        var ns = "kick-late-release-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            using var exiting = JobRunnerHost.CreateForTests(ns, composeRuntime: false, workerRoot: root);
            using var kicked = JobRunnerHost.ForNamespace(ns);
            using var machine = MachineDatabase.Open(root);
            var known = new KnownProjectRegistry(machine);
            var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0));
            var ownership = WorkspaceOwnership.Bootstrap(root);
            var runtimes = exiting.CreateRuntimeRegistry(catalog, (jobs, _) =>
                new WorkerRecoveryCoordinator(new WorkerRecovery(jobs), new WorkspaceCleaner(ownership)));
            Assert.True(exiting.TryAcquireOwnership());
            exiting.Start();

            var path = Path.Combine(root, "retiring-owner.fwdata");
            File.WriteAllText(path, "placeholder");
            var project = new ProjectLocator(path, "retiring-owner");
            var key = ProjectWorkspaceKey.Compute(project);
            known.Record(key, path, DateTimeOffset.UtcNow);
            var activity = new WorkerRuntime.SweepActivity();
            var empty = await WorkerRuntime.SweepOnceAsync(known, runtimes, exiting.ProjectLanes,
                new RunnerOptions { Root = root }, new FakeInvoker(), "retiring-owner",
                CancellationToken.None, activity);
            Assert.Null(empty.JobId);
            Assert.True(runtimes.TryGet(key, out _));
            Assert.False(activity.HasActiveWork);
            Assert.True(activity.TryRetire());
            Assert.False(activity.TryBeginSweep());

            var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var clock = new RetryTimeProvider(DateTimeOffset.UtcNow);
            Task? disposing = null;
            Task<bool>? retrying = null;
            try
            {
                exiting.SetRuntimeRegistryDisposeOverrideForTests(registry =>
                {
                    disposalEntered.TrySetResult();
                    try { releaseDisposal.Task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult(); }
                    finally { registry.Dispose(); }
                });
                disposing = Task.Run(exiting.Dispose);
                await disposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

                const string jobId = "queued-during-retirement";
                using (var enqueueStore = catalog.OpenOwned(project, TimeSpan.FromSeconds(10)))
                {
                    var jobs = new JobRepository(enqueueStore);
                    jobs.Create(jobId, key, "probe", "{}", JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
                    Assert.Equal(JobStatus.Queued, jobs.Get(jobId)!.Status);
                }

                retrying = WorkerRuntime.TryAcquireOwnershipWithRetryAsync(kicked, clock,
                    wakeProjectPath: path);
                await clock.WaitForTimerAsync();
                clock.Advance(TimeSpan.FromSeconds(3));
                await clock.WaitForSecondTimerAsync();

                Assert.False(retrying.IsCompleted);
                Assert.False(kicked.IsOwner);
                Assert.False(releaseDisposal.Task.IsCompleted);
                Assert.False(disposing.IsCompleted);

                releaseDisposal.TrySetResult();
                await disposing.WaitAsync(TimeSpan.FromSeconds(10));
                clock.Advance(TimeSpan.FromMilliseconds(50));
                Assert.True(await retrying.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.True(kicked.IsOwner);
                using var reopened = catalog.OpenOwned(project, TimeSpan.FromSeconds(10));
                Assert.Equal(JobStatus.Queued, new JobRepository(reopened).Get(jobId)!.Status);
            }
            finally
            {
                releaseDisposal.TrySetResult();
                clock.Advance(TimeSpan.FromSeconds(3));
                try
                {
                    if (disposing is not null) await disposing.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally
                {
                    if (retrying is not null) await retrying.WaitAsync(TimeSpan.FromSeconds(10));
                }
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SeveralQueuedJobsLeaveAtMostOneKickedWaiterBehindAnOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-wake-waiters-" + Guid.NewGuid().ToString("N"));
        var ns = "wake-waiters-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "busy-owner.fwdata");
            File.WriteAllText(path, "placeholder");
            var project = new ProjectLocator(path, "busy-owner");
            var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0));
            using (var database = catalog.OpenOwned(project))
            {
                var jobs = new JobRepository(database);
                for (var index = 0; index < 3; index++)
                    jobs.Create("queued-" + index, ProjectWorkspaceKey.Compute(project), "probe", "{}",
                        JobTimestamp.FormatUtc(DateTimeOffset.UtcNow));
            }

            using var owner = JobRunnerHost.ForNamespace(ns);
            Assert.True(owner.TryAcquireOwnership());
            var kicked = Enumerable.Range(0, 3).Select(_ => JobRunnerHost.ForNamespace(ns)).ToArray();
            try
            {
                var clocks = kicked.Select(_ => new RetryTimeProvider(DateTimeOffset.UtcNow)).ToArray();
                var attempts = kicked.Select((host, index) => WorkerRuntime.TryAcquireOwnershipWithRetryAsync(
                    host, clocks[index], path, extraWaitLimit: TimeSpan.FromSeconds(10))).ToArray();
                await Task.WhenAll(clocks.Select(clock => clock.WaitForTimerAsync()));
                foreach (var clock in clocks) clock.Advance(TimeSpan.FromSeconds(3));

                var completed = await Task.WhenAll(attempts.Select(async attempt =>
                    await Task.WhenAny(attempt, Task.Delay(TimeSpan.FromSeconds(2))) == attempt));
                Assert.Equal(2, completed.Count(done => done));
                Assert.Equal(2, attempts.Count(attempt => attempt.IsCompletedSuccessfully && !attempt.Result));
                Assert.Equal(1, attempts.Count(attempt => !attempt.IsCompleted));

                owner.Dispose();
                var waiter = Array.FindIndex(attempts, attempt => !attempt.IsCompleted);
                clocks[waiter].Advance(TimeSpan.FromMilliseconds(50));
                Assert.True(await attempts[waiter].WaitAsync(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                owner.Dispose();
                foreach (var host in kicked) host.Dispose();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AContendedWakeProbeGivesUpAtTheExtraWaitBound()
    {
        var ns = "wake-bound-" + Guid.NewGuid().ToString("N");
        using var owner = JobRunnerHost.ForNamespace(ns);
        using var kicked = JobRunnerHost.ForNamespace(ns);
        Assert.True(owner.TryAcquireOwnership());
        var clock = new RetryTimeProvider(DateTimeOffset.UtcNow);
        var warnings = new List<string>();
        var attempts = 0;
        var retrying = WorkerRuntime.TryAcquireOwnershipWithRetryAsync(kicked, clock,
            wakeProjectPath: "unreadable.fwdata", extraWaitLimit: TimeSpan.FromSeconds(4),
            reportWarning: warnings.Add,
            queuedWorkProbe: _ =>
            {
                attempts++;
                throw new MotifStoreLockException("persistent lock", new IOException("locked"));
            });

        await clock.WaitForTimerAsync();
        clock.Advance(TimeSpan.FromSeconds(3));
        await clock.WaitForSecondTimerAsync();
        Assert.False(retrying.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(4));

        Assert.False(await retrying.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(attempts > 0);
        Assert.Single(warnings);
        Assert.Contains("queued work", warnings[0], StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RetryTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _timerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondTimerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _timerCount;
        private DateTimeOffset _now = now;
        private RetryTimer? _timer;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_gate) return _now.UtcTicks;
        }

        public async Task WaitForTimerAsync() => await _timerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public async Task WaitForSecondTimerAsync() =>
            await _secondTimerCreated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Advance(TimeSpan elapsed)
        {
            TimerCallback? callback = null;
            object? state = null;
            lock (_gate)
            {
                _now += elapsed;
                if (_timer is not null && _timer.TakeDue(_now, out var dueCallback, out var dueState))
                {
                    callback = dueCallback;
                    state = dueState;
                }
            }
            callback?.Invoke(state);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            RetryTimer timer;
            lock (_gate)
            {
                timer = new RetryTimer(this, callback, state);
                timer.Schedule(_now, dueTime, period);
                _timer = timer;
            }
            _timerCreated.TrySetResult();
            if (Interlocked.Increment(ref _timerCount) >= 2) _secondTimerCreated.TrySetResult();
            return timer;
        }

        private bool Change(RetryTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (timer.IsDisposed) return false;
                timer.Schedule(_now, dueTime, period);
                return true;
            }
        }

        private void Dispose(RetryTimer timer)
        {
            lock (_gate) timer.MarkDisposed();
        }

        private sealed class RetryTimer(RetryTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _dueAt;
            private TimeSpan _period;

            internal bool IsDisposed { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => owner.Change(this, dueTime, period);

            public void Dispose() => owner.Dispose(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            internal void Schedule(DateTimeOffset current, TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : current + dueTime;
                _period = period;
            }

            internal bool TakeDue(DateTimeOffset current, out TimerCallback timerCallback, out object? timerState)
            {
                timerCallback = callback;
                timerState = state;
                if (IsDisposed || _dueAt is not { } dueAt || dueAt > current) return false;
                _dueAt = _period > TimeSpan.Zero ? dueAt + _period : null;
                return true;
            }

            internal void MarkDisposed()
            {
                IsDisposed = true;
                _dueAt = null;
            }
        }
    }
}
