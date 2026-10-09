using System.Threading;

namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// Holds every machine PanGloss capacity slot until disposed.
/// </summary>
internal sealed class MachineSlotLease : IDisposable
{
    private readonly IReadOnlyList<WorkerMutexOwner> _owners;
    private readonly IReadOnlyList<int> _slotIndexes;
    private readonly Action<int>? _onDisposed;
    private bool _disposed;

    internal MachineSlotLease(WorkerMutexOwner owner, int slotIndex, string jobId, Action<int>? onDisposed)
        : this([owner], [slotIndex], jobId, onDisposed)
    {
    }

    internal MachineSlotLease(IReadOnlyList<WorkerMutexOwner> owners, IReadOnlyList<int> slotIndexes,
        string jobId, Action<int>? onDisposed)
    {
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(slotIndexes);
        if (owners.Count == 0 || owners.Count != slotIndexes.Count)
            throw new ArgumentException("Each leased slot requires one lock owner.", nameof(slotIndexes));
        _owners = owners.ToArray();
        _slotIndexes = slotIndexes.ToArray();
        JobId = jobId ?? throw new ArgumentNullException(nameof(jobId));
        _onDisposed = onDisposed;
    }

    /// <summary>The machine slots this lease holds.</summary>
    public IReadOnlyList<int> SlotIndexes => _slotIndexes;

    /// <summary>The first machine slot this lease holds.</summary>
    public int SlotIndex => _slotIndexes[0];

    /// <summary>The job id recorded against this slot for as long as it is held.</summary>
    public string JobId { get; }

    /// <summary>Releases the underlying machine-global mutex so another job may be admitted.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Exception? failure = null;
        for (var i = _owners.Count - 1; i >= 0; i--)
        {
            try { _owners[i].Dispose(); }
            catch (Exception exception) { failure ??= exception; }
            finally { _onDisposed?.Invoke(_slotIndexes[i]); }
        }
        if (failure is not null)
            throw new InvalidOperationException("A machine capacity lock could not be released.", failure);
    }
}
