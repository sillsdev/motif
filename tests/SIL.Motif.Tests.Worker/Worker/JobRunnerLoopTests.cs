using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>Covers what the runner's work loop does with a queued row, in-process.</summary>
/// <remarks>
/// The loop's own decisions are covered here where they are cheap to provoke; that a real runner
/// executable performs them is <c>RunnerSpineTests</c>' subject.
/// </remarks>
public sealed class JobRunnerLoopTests : IDisposable
{
    private const string Project = "project-key";
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-loop-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;

    public JobRunnerLoopTests()
    {
        Directory.CreateDirectory(_root);
        var locator = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), locator,
            MotifSchema.CurrentSchema, new Version(1, 0));
    }

    [Fact]
    public async Task AQueuedJobIsClaimedDispatchedAndCompleted()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());
        var seen = new List<string>();

        await Loop((job, _) => { seen.Add(job.JobId); return Task.FromResult<JobOutcome?>(JobOutcome.Completed); })
            .RunUntilIdleAsync(CancellationToken.None);

        Assert.Equal(new[] { "job-1" }, seen);
        Assert.Equal(JobStatus.Completed, jobs.Get("job-1")!.Status);
    }

    [Fact]
    public async Task AKindWithNoHandlerFailsTheJobRatherThanSpinningOnIt()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "nobody-handles-this", "{}", Stamp());

        var loop = new JobRunnerLoop(new JobClaims(_database), Project, "runner-a",
            TimeSpan.FromMinutes(5),
            TimeSpan.Zero, new Dictionary<string, JobRunnerLoop.Handler>(StringComparer.Ordinal));
        await loop.RunUntilIdleAsync(CancellationToken.None);

        var job = jobs.Get("job-1")!;
        // Failed rather than left queued: a row nothing can run is a row every later poll would re-claim.
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("nobody-handles-this", job.ResultJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AHandlerThatThrowsFailsTheJobCarryingTheReason()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());

        await Loop((_, _) => throw new InvalidOperationException("the capture exploded"))
            .RunUntilIdleAsync(CancellationToken.None);

        var job = jobs.Get("job-1")!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("the capture exploded", job.ResultJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLoopHeartbeatsWhileAHandlerRuns()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());
        var initialTime = DateTimeOffset.Parse(Stamp());
        using var clock = new ManualTimeProvider(initialTime);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var loop = Loop(async (_, _) =>
        {
            handlerStarted.SetResult();
            await releaseHandler.Task;
            return JobOutcome.Completed;
        }, lease: TimeSpan.FromSeconds(6), timeProvider: clock);
        var running = loop.RunUntilIdleAsync(CancellationToken.None);

        try
        {
            await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var initialLease = jobs.Get("job-1")!.LeaseUntilUtc;
            await clock.WaitForTimerCountAsync(1);
            clock.Advance(TimeSpan.FromSeconds(1));
            await clock.WaitForTimerCountAsync(2);

            var renewedLease = jobs.Get("job-1")!.LeaseUntilUtc;
            Assert.NotNull(initialLease);
            Assert.NotNull(renewedLease);
            Assert.True(DateTimeOffset.Parse(renewedLease) > DateTimeOffset.Parse(initialLease));
        }
        finally
        {
            releaseHandler.TrySetResult();
            await running;
        }
    }

    [Fact]
    public async Task TheLoopDoesNotAbandonALeasedJobWhenItStops()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());
        using var stopping = new CancellationTokenSource();

        await Loop(async (_, _) => { stopping.Cancel(); await Task.Delay(50); return JobOutcome.Completed; })
            .RunUntilIdleAsync(stopping.Token);

        // Cancelled mid-handler, the row must still reach a terminal state rather than stay leased.
        Assert.True(jobs.Get("job-1")!.Status is JobStatus.Cancelled or JobStatus.Failed
            or JobStatus.Completed);
    }

    [Fact]
    public async Task ACancellationRequestReachesARunningHandlerThroughTheHeartbeatAndLandsItCancelled()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());
        var started = new TaskCompletionSource();

        var running = Loop(async (_, token) =>
        {
            started.TrySetResult();
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return JobOutcome.Completed;
        }, lease: TimeSpan.FromMilliseconds(300)).RunUntilIdleAsync(CancellationToken.None);

        await started.Task;
        // Set from outside the handler, exactly like `jobs cancel` does against a running row.
        jobs.RequestCancellation("job-1");
        await running;

        var job = jobs.Get("job-1")!;
        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(JobFailureCategory.Cancellation, job.FailureCategory);
    }

    [Fact]
    public async Task AHandlerStoppedWhileItsRowWaitsBehindTheLaneLandsItCancelled()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());

        await Loop((job, _) =>
        {
            job.Transition(JobStatus.WaitingForBaseline);
            throw new OperationCanceledException();
        }).RunUntilIdleAsync(CancellationToken.None);

        // Left parked, the row is active forever: nothing reclaims it, and it keeps every later runner alive.
        Assert.Equal(JobStatus.Cancelled, jobs.Get("job-1")!.Status);
    }

    [Fact]
    public async Task AHandlerThatFailsWhileItsRowWaitsBehindTheLaneLandsItFailed()
    {
        var jobs = new JobRepository(_database);
        jobs.Create("job-1", Project, "demo", "{}", Stamp());

        await Loop((job, _) =>
        {
            job.Transition(JobStatus.WaitingForBaseline);
            throw new InvalidOperationException("the lane went away");
        }).RunUntilIdleAsync(CancellationToken.None);

        var job = jobs.Get("job-1")!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("the lane went away", job.ResultJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyQueueIsNotAnError()
    {
        var jobs = new JobRepository(_database);

        await Loop((_, _) => Task.FromResult<JobOutcome?>(JobOutcome.Completed)).RunUntilIdleAsync(CancellationToken.None);

        Assert.Empty(jobs.ListActive(Project));
    }

    private JobRunnerLoop Loop(JobRunnerLoop.Handler handler, TimeSpan? lease = null,
        TimeProvider? timeProvider = null) =>
        new(new JobClaims(_database), Project, "runner-a", lease ?? TimeSpan.FromMinutes(5), TimeSpan.Zero,
            new Dictionary<string, JobRunnerLoop.Handler>(StringComparer.Ordinal) { ["demo"] = handler }, timeProvider);

    private static string Stamp() =>
        JobTimestamp.FormatUtc(DateTimeOffset.UtcNow);

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider, IDisposable
    {
        private readonly object _gate = new();
        private readonly List<ManualTimer> _timers = [];
        private readonly SemaphoreSlim _timerCreated = new(0);
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            lock (_gate) return _now;
        }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp()
        {
            lock (_gate) return _now.UtcTicks;
        }

        public async Task WaitForTimerCountAsync(int count)
        {
            while (true)
            {
                lock (_gate)
                    if (_timers.Count >= count) return;
                await _timerCreated.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            List<(TimerCallback Callback, object? State)> ready = [];
            lock (_gate)
            {
                _now += elapsed;
                foreach (var timer in _timers)
                    if (timer.TakeDue(_now, out var callback, out var state))
                        ready.Add((callback, state));
            }
            foreach (var (callback, state) in ready) callback(state);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state,
            TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (_gate)
            {
                timer.Schedule(_now, dueTime, period);
                _timers.Add(timer);
            }
            _timerCreated.Release();
            return timer;
        }

        private bool Change(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                if (timer.IsDisposed) return false;
                timer.Schedule(_now, dueTime, period);
                return true;
            }
        }

        private void Dispose(ManualTimer timer)
        {
            lock (_gate) timer.MarkDisposed();
        }

        public void Dispose()
        {
            lock (_gate)
                foreach (var timer in _timers) timer.MarkDisposed();
            _timerCreated.Dispose();
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
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

            internal void Schedule(DateTimeOffset now, TimeSpan dueTime, TimeSpan period)
            {
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : now + dueTime;
                _period = period;
            }

            internal bool TakeDue(DateTimeOffset now, out TimerCallback timerCallback, out object? timerState)
            {
                timerCallback = callback;
                timerState = state;
                if (IsDisposed || _dueAt is not { } dueAt || dueAt > now) return false;
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
