using System.Diagnostics;
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
    private const string CrossTempRootVariable = "MOTIF_TEST_CROSS_TEMP_ROOT";
    private const string CrossTempOwnerVariable = "MOTIF_TEST_CROSS_TEMP_OWNER";

    [Fact]
    public void CurrentOwnerNameIncludesThePlatformUserAndConfiguredRoot()
    {
        var name = JobRunnerHost.GetOwnerMutexName();

        if (OperatingSystem.IsWindows())
            Assert.StartsWith(@"Local\SIL.Motif.Worker.Owner.", name, StringComparison.Ordinal);
        else
            Assert.Matches("^uid-[0-9]+$", WorkerIdentity.GetCurrentUserNamespace());
        Assert.Contains(WorkerIdentity.GetCurrentUserNamespace(), name, StringComparison.Ordinal);
        Assert.Contains(".Root.", name, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentRootsCanOwnRunnersAtTheSameTimeForOneUser()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-owner-roots-" + Guid.NewGuid().ToString("N"));
        var firstRoot = Path.Combine(root, "first");
        var secondRoot = Path.Combine(root, "second");
        var identity = "test-owner-" + Guid.NewGuid().ToString("N");
        using var first = JobRunnerHost.ForRoot(firstRoot, identity);
        using var second = JobRunnerHost.ForRoot(secondRoot, identity);
        using var duplicate = JobRunnerHost.ForRoot(firstRoot, identity);

        Assert.NotEqual(first.OwnerName, second.OwnerName);
        Assert.Equal(first.OwnerName, duplicate.OwnerName);
        Assert.True(first.TryAcquireOwnership());
        Assert.True(second.TryAcquireOwnership());
        Assert.False(duplicate.TryAcquireOwnership());
    }

    [UnixWorkerLockFact]
    public void PhysicalAliasesShareOneRunnerOwner()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-owner-physical-alias-" + Guid.NewGuid().ToString("N"));
        var realParent = Path.Combine(root, "real");
        var aliasParent = Path.Combine(root, "alias");
        var realRoot = Path.Combine(realParent, "installation");
        var aliasRoot = Path.Combine(aliasParent, "installation");
        var ownerNamespace = "physical-alias-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(realRoot);
            Directory.CreateSymbolicLink(aliasParent, realParent);
            using var first = JobRunnerHost.ForRoot(realRoot, ownerNamespace);
            using var alias = JobRunnerHost.ForRoot(aliasRoot, ownerNamespace);

            Assert.Equal(first.OwnerName, alias.OwnerName);
            Assert.True(first.TryAcquireOwnership());
            Assert.False(alias.TryAcquireOwnership());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [UnixWorkerLockFact]
    public async Task SameRootOwnerIsSharedAcrossDifferentTempDirectories()
    {
        if (Environment.GetEnvironmentVariable(CrossTempRootVariable) is { Length: > 0 } childRoot &&
            Environment.GetEnvironmentVariable(CrossTempOwnerVariable) is { Length: > 0 } childNamespace)
        {
            using var childOwner = JobRunnerHost.ForRoot(childRoot, childNamespace);
            Assert.False(childOwner.TryAcquireOwnership());
            return;
        }

        var baseRoot = Path.Combine(Path.GetTempPath(), "motif-owner-cross-temp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseRoot);
        var alternateTemp = Path.Combine(baseRoot, "alternate-temp");
        Directory.CreateDirectory(alternateTemp);
        var workerRoot = Path.Combine(baseRoot, "worker-root");
        var ownerNamespace = "cross-temp-test-" + Guid.NewGuid().ToString("N");
        using var owner = JobRunnerHost.ForRoot(workerRoot, ownerNamespace);
        Assert.True(owner.TryAcquireOwnership());

        var projectPath = Path.Combine(FindRepositoryRoot(), "tests", "SIL.Motif.Tests.Worker",
            "SIL.Motif.Tests.Worker.csproj");
        var childStart = DotnetTestProcess.CreateTestStartInfo(projectPath,
            "FullyQualifiedName=SIL.Motif.Tests.Worker.JobRunnerHostOwnershipTests." +
            "SameRootOwnerIsSharedAcrossDifferentTempDirectories");
        childStart.Environment[CrossTempRootVariable] = workerRoot;
        childStart.Environment[CrossTempOwnerVariable] = ownerNamespace;
        childStart.Environment["TMPDIR"] = alternateTemp;
        childStart.Environment["TMP"] = alternateTemp;
        childStart.Environment["TEMP"] = alternateTemp;
        using var child = Process.Start(childStart)
            ?? throw new InvalidOperationException("Could not start the cross-temp-root test process.");
        var output = child.StandardOutput.ReadToEndAsync();
        var error = child.StandardError.ReadToEndAsync();
        try
        {
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
            Assert.True(child.ExitCode == 0,
                "Child test failed. stdout: " + await output + " stderr: " + await error);
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync();
            }
            Directory.Delete(baseRoot, recursive: true);
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

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Motif.sln")))
            current = current.Parent;
        return current?.FullName ?? throw new DirectoryNotFoundException("Could not find Motif.sln.");
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
