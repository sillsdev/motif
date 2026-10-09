using System.Collections.Concurrent;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Admits one PanGloss job at a time across worker processes against shared capacity slots.
/// </summary>
/// <remarks>
/// The slots are machine-wide by default. Test processes can set
/// <c>MOTIF_TEST_PAN_GLOSS_SLOT_NAMESPACE</c> to isolate their slots; child processes inherit that scope.
/// A job is admitted in its queue's submission order and holds every capacity lock for its full run.
/// Independent queues using the same lock names therefore cannot run parser jobs together; acquiring
/// every name also coordinates with workers that lease one name per job (pinned by
/// `RunAsync_AcrossTwoQueuesNeverExceedsOneAdmittedJob` and
/// `RunAsync_AcrossProcessesWaitsUntilEveryMachineSlotIsReleased`).
/// </remarks>
public sealed class MachinePanGlossQueue : IDisposable
{
    private static readonly string[] MachineWideSlotNames =
    {
        "Global\\MotifPanGlossSlot-0",
        "Global\\MotifPanGlossSlot-1",
    };

    internal const string TestSlotNamespaceVariable = "MOTIF_TEST_PAN_GLOSS_SLOT_NAMESPACE";

    // Machine leases are locks, not events, so polling is how a freed slot is noticed.
    private static readonly TimeSpan SlotPollInterval = TimeSpan.FromMilliseconds(10);

    private readonly IReadOnlyList<string> _slotNames;
    private readonly ConcurrentDictionary<int, string> _slotOwnership = new();
    private readonly object _gate = new();
    private readonly LinkedList<QueuedJob> _queue = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _runner;
    private bool _disposed;

    /// <summary>Creates a queue for the machine-wide slots, or an isolated test-process scope when configured.</summary>
    public MachinePanGlossQueue() : this(GetDefaultSlotNames(
        Environment.GetEnvironmentVariable(TestSlotNamespaceVariable)))
    {
    }

    /// <summary>Creates a queue against explicit slot names, so callers can isolate their own leases.</summary>
    internal MachinePanGlossQueue(IReadOnlyList<string> slotNames)
    {
        if (slotNames is null || slotNames.Count == 0)
            throw new ArgumentException("At least one machine slot name is required.", nameof(slotNames));
        if (slotNames.Distinct(StringComparer.Ordinal).Count() != slotNames.Count)
            throw new ArgumentException("Machine slot names must be unique.", nameof(slotNames));
        _slotNames = slotNames;
        _runner = RunAsync();
    }

    internal static IReadOnlyList<string> GetDefaultSlotNames(string? testProcessNamespace)
    {
        if (string.IsNullOrWhiteSpace(testProcessNamespace)) return MachineWideSlotNames;
        return new[]
        {
            $"Global\\MotifPanGlossSlot-{testProcessNamespace}-0",
            $"Global\\MotifPanGlossSlot-{testProcessNamespace}-1",
        };
    }

    /// <summary>The job id currently recorded against each held slot, for diagnosing contention.</summary>
    internal IReadOnlyDictionary<int, string> SlotOwnership => _slotOwnership;

    /// <summary>Observes each job object the moment a job is admitted into it. Set only by tests.</summary>
    internal Action<PanGlossContainmentJob>? JobAdmitted { get; set; }

    /// <summary>Observes the first failed slot acquisition for each job.</summary>
    internal Action<string>? SlotWaitStarted { get; set; }

    /// <summary>
    /// Queues <paramref name="work"/> under <paramref name="jobId"/> and returns its result once the
    /// job has been admitted to a machine slot and has run to completion.
    /// </summary>
    public Task<T> RunAsync<T>(string jobId, Func<PanGlossContainmentJob, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Required.", nameof(jobId));
        ArgumentNullException.ThrowIfNull(work);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new QueuedJob<T>(jobId, work, cancellationToken, completion);
        lock (_gate)
        {
            ThrowIfDisposed();
            job.Node = _queue.AddLast(job);
            RegisterCancellation(job);
        }
        _signal.Release();
        return completion.Task;
    }

    public void Dispose()
    {
        QueuedJob[] queued;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            queued = _queue.ToArray();
            _queue.Clear();
        }
        _shutdown.Cancel();
        _signal.Release();
        foreach (var job in queued)
        {
            job.Registration.Dispose();
            job.Cancel(CancellationToken.None);
        }
        try { _runner.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        _shutdown.Dispose();
        _signal.Dispose();
    }

    private async Task RunAsync()
    {
        while (true)
        {
            await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            QueuedJob? job;
            lock (_gate)
            {
                if (_disposed) return;
                var node = _queue.First;
                job = node?.Value;
                if (node is not null) _queue.Remove(node);
            }
            if (job is null) continue;
            job.Registration.Dispose();
            var linked = CancellationTokenSource.CreateLinkedTokenSource(job.CancellationToken, _shutdown.Token);
            if (linked.Token.IsCancellationRequested)
            {
                job.Cancel(linked.Token);
                linked.Dispose();
                continue;
            }
            MachineSlotLease lease;
            try
            {
                lease = await AcquireMachineCapacityAsync(job.JobId, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                job.Cancel(linked.Token);
                linked.Dispose();
                continue;
            }
            catch (IOException exception)
            {
                linked.Dispose();
                job.Fail(exception);
                continue;
            }
            _ = RunAdmittedJobAsync(job, lease, linked);
        }
    }

    private async Task RunAdmittedJobAsync(QueuedJob job, MachineSlotLease lease,
        CancellationTokenSource linked)
    {
        try
        {
            using var cpuJob = PanGlossContainment.CreateJob();
            JobAdmitted?.Invoke(cpuJob);
            await job.ExecuteAsync(cpuJob, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            lease.Dispose();
            linked.Dispose();
        }
    }

    private async Task<MachineSlotLease> AcquireMachineCapacityAsync(string jobId, CancellationToken cancellationToken)
    {
        // One owner per slot per wait: ownership is per-thread, and per-poll owners would churn threads.
        var owners = new List<WorkerMutexOwner>(_slotNames.Count);
        var transferred = false;
        var waitReported = false;
        try
        {
            foreach (var slotName in _slotNames)
                owners.Add(new WorkerMutexOwner(slotName, machineWide: true));

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var acquired = new List<int>(_slotNames.Count);
                for (var i = 0; i < owners.Count; i++)
                {
                    if (!owners[i].TryAcquire()) break;
                    acquired.Add(i);
                }
                if (acquired.Count == owners.Count)
                {
                    var lease = new MachineSlotLease(owners, acquired, jobId,
                        released => _slotOwnership.TryRemove(released, out _));
                    foreach (var index in acquired) _slotOwnership[index] = jobId;
                    transferred = true;
                    return lease;
                }
                foreach (var index in acquired) owners[index].Release();
                if (!waitReported)
                {
                    SlotWaitStarted?.Invoke(jobId);
                    waitReported = true;
                }
                await Task.Delay(SlotPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (!transferred)
                foreach (var owner in owners) owner.Dispose();
        }
    }

    private void RegisterCancellation(QueuedJob job)
    {
        if (!job.CancellationToken.CanBeCanceled) return;
        job.Registration = job.CancellationToken.Register(() =>
        {
            lock (_gate)
            {
                if (job.Node is { List: not null } node) _queue.Remove(node);
            }
            job.Cancel(job.CancellationToken);
        });
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MachinePanGlossQueue));
    }

    private abstract class QueuedJob
    {
        protected QueuedJob(string jobId, CancellationToken cancellationToken)
        {
            JobId = jobId;
            CancellationToken = cancellationToken;
        }

        public string JobId { get; }
        public CancellationToken CancellationToken { get; }
        public LinkedListNode<QueuedJob>? Node { get; set; }
        public CancellationTokenRegistration Registration { get; set; }

        public abstract Task ExecuteAsync(PanGlossContainmentJob cpuJob, CancellationToken linkedToken);
        public abstract void Cancel(CancellationToken token);
        public abstract void Fail(Exception exception);
    }

    private sealed class QueuedJob<T> : QueuedJob
    {
        private readonly Func<PanGlossContainmentJob, CancellationToken, Task<T>> _work;
        private readonly TaskCompletionSource<T> _completion;

        public QueuedJob(string jobId, Func<PanGlossContainmentJob, CancellationToken, Task<T>> work,
            CancellationToken cancellationToken, TaskCompletionSource<T> completion)
            : base(jobId, cancellationToken)
        {
            _work = work;
            _completion = completion;
        }

        public override async Task ExecuteAsync(PanGlossContainmentJob cpuJob, CancellationToken linkedToken)
        {
            try
            {
                var result = await _work(cpuJob, linkedToken).ConfigureAwait(false);
                _completion.TrySetResult(result);
            }
            catch (OperationCanceledException) when (linkedToken.IsCancellationRequested)
            {
                _completion.TrySetCanceled(linkedToken);
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
            }
        }

        public override void Cancel(CancellationToken token) => _completion.TrySetCanceled(token);

        public override void Fail(Exception exception) => _completion.TrySetException(exception);
    }
}
