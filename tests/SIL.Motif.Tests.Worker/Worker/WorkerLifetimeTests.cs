using System.Collections.Concurrent;
using SIL.Motif.Worker;
using Xunit;

namespace SIL.Motif.Tests.Worker;

/// <summary>
/// Covers <see cref="WorkerLifetime"/> in isolation: idleness is a predicate the caller supplies — the
/// sweep's own finding, in production — rather than a cached lease, so these drive that predicate
/// directly instead of standing up a runner.
/// </summary>
public sealed class WorkerLifetimeTests
{
    [Fact]
    public async Task IdleWithNothingQueuedExitsWithinItsTimeout()
    {
        using var shutdown = new CancellationTokenSource();
        using var clock = new ManualWorkerClock();

        var running = new WorkerLifetime(clock).RunUntilIdleAsync(
            TimeSpan.FromMilliseconds(100), () => false, shutdown.Token);

        Assert.True(await clock.WaitForDelayRequestAsync());
        clock.AdvanceNextDelay();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(running.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task StaysAliveWhileWorkIsActiveAndDoesNotExitMidJob()
    {
        using var shutdown = new CancellationTokenSource();
        using var clock = new ManualWorkerClock();
        var busy = 1;

        var running = new WorkerLifetime(clock).RunUntilIdleAsync(
            TimeSpan.FromMilliseconds(100), () => Volatile.Read(ref busy) == 1, shutdown.Token);

        Assert.True(await clock.WaitForDelayRequestAsync());
        clock.AdvanceNextDelay();
        Assert.True(await clock.WaitForDelayRequestAsync());
        Assert.False(running.IsCompleted);
        Interlocked.Exchange(ref busy, 0);
        clock.AdvanceNextDelay();
        Assert.True(await clock.WaitForDelayRequestAsync());
        clock.AdvanceNextDelay();

        await running.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(running.IsCompletedSuccessfully);
    }

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

        public Task<bool> WaitForDelayRequestAsync() => _delayRequests.WaitAsync(TimeSpan.FromSeconds(5));

        public void AdvanceNextDelay()
        {
            if (!_waiters.TryDequeue(out var waiter))
                throw new InvalidOperationException("No worker delay is waiting to advance.");
            Interlocked.Add(ref _ticks, waiter.Delay.Ticks);
            waiter.Completion.TrySetResult(true);
        }

        public void Dispose() => _delayRequests.Dispose();
    }
}
