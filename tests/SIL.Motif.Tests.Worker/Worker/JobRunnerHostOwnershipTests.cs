using SIL.Motif.Worker;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Covers <see cref="Program.TryAcquireOwnershipWithRetryAsync"/>: the reason a runner the CLI kicks does
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
        var acquired = await SIL.Motif.Worker.Program.TryAcquireOwnershipWithRetryAsync(kicked);

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
        var retrying = SIL.Motif.Worker.Program.TryAcquireOwnershipWithRetryAsync(kicked, clock);

        await clock.WaitForTimerAsync();
        Assert.True(exiting.IsOwner);
        Assert.False(kicked.IsOwner);
        exiting.Dispose();
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.True(await retrying.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(kicked.IsOwner);
    }

    private sealed class RetryTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _timerCreated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
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
