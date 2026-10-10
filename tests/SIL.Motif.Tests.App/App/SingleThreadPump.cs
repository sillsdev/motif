using System.Collections.Concurrent;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Runs asynchronous test work on one thread whose synchronization context pumps posted continuations, as the
/// App's UI thread does.
/// </summary>
/// <remarks>
/// View models such as <c>SelectionLinePages</c> are single-thread by design: the App drives them from its UI
/// thread, so their continuations resume there and never overlap. A test that drives them from an xUnit test
/// body resumes on the thread pool instead, and two continuations can then touch the same unsynchronized state
/// at once, pinned by
/// <see cref="SelectionLinePagesThreadTests.PoolThreadsReadingLinePagesWhileThePumpRunsCorruptItsWaitingPages"/>.
/// Work that already runs on a pump thread, or on the Avalonia headless thread, runs inline.
/// </remarks>
internal static class SingleThreadPump
{
    [ThreadStatic] private static bool _onPump;

    /// <summary>Runs <paramref name="work"/> on a pumping thread and completes with its outcome.</summary>
    public static Task RunAsync(Func<Task> work) => RunAsync(async () =>
    {
        await work().ConfigureAwait(true);
        return true;
    });

    /// <summary>Runs <paramref name="work"/> on a pumping thread and returns or throws its result.</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_onPump || AvaloniaHeadlessPlatform.IsCurrentThread) return work();
        var outcome = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() => Pump(work, outcome)) { IsBackground = true, Name = "Single-thread pump" };
        thread.Start();
        return outcome.Task;
    }

    private static void Pump<T>(Func<Task<T>> work, TaskCompletionSource<T> outcome)
    {
        _onPump = true;
        var context = new PumpContext();
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            Task<T> task;
            try { task = work(); }
            catch (Exception exception) { task = Task.FromException<T>(exception); }
            task.ContinueWith(static (_, state) => ((PumpContext)state!).Complete(), context,
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var escaped = context.Run();
            if (escaped.Count > 0) outcome.TrySetException(escaped);
            else if (task.IsCanceled) outcome.TrySetCanceled();
            else if (task.Exception is { } failure) outcome.TrySetException(failure.InnerExceptions);
            else outcome.TrySetResult(task.Result);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
            _onPump = false;
        }
    }

    private sealed class PumpContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly object _gate = new();
        private bool _completed;

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_gate)
            {
                if (!_completed)
                {
                    _queue.Add((d, state));
                    return;
                }
            }
            // A continuation that outlives the work it belonged to has no pump left; the pool runs it as it would have.
            ThreadPool.QueueUserWorkItem(static item => item.Callback(item.State), (Callback: d, State: state),
                preferLocal: false);
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (_onPump && Current == this)
            {
                d(state);
                return;
            }
            using var done = new ManualResetEventSlim();
            Exception? failure = null;
            Post(_ =>
            {
                try { d(state); }
                catch (Exception exception) { failure = exception; }
                finally { done.Set(); }
            }, null);
            done.Wait();
            if (failure is not null) throw failure;
        }

        public override SynchronizationContext CreateCopy() => this;

        public void Complete()
        {
            lock (_gate)
            {
                _completed = true;
                _queue.CompleteAdding();
            }
        }

        public IReadOnlyList<Exception> Run()
        {
            var escaped = new List<Exception>();
            foreach (var (callback, state) in _queue.GetConsumingEnumerable())
            {
                try { callback(state); }
                catch (Exception exception) { escaped.Add(exception); }
            }
            return escaped;
        }
    }
}
