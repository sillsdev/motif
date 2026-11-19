using System.Diagnostics;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>Excludes a Source index read from LibLCM startup on the same project across Motif processes.</summary>
/// <remarks>Hold on one thread and release before loading a private copy or indexing its contents.</remarks>
internal sealed class ProjectStartupGate : IDisposable
{
    internal static readonly AsyncLocal<Action?> WaitingForWorkerRead = new();
    private static readonly TimeSpan WaitBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CancellationInterval = TimeSpan.FromMilliseconds(50);
    private static readonly NamedWaitHandleOptions Scope = new() { CurrentSessionOnly = false };
    private const string OpenMarker = ".Open";
    private const string IndexMarker = ".SourceIndex";

    private readonly Mutex _mutex;
    private readonly Mutex _holderMarker;

    private ProjectStartupGate(Mutex mutex, Mutex holderMarker)
    {
        _mutex = mutex;
        _holderMarker = holderMarker;
    }

    internal static ProjectStartupGate? Enter(string projectPath, CancellationToken cancellationToken = default,
        bool sourceIndex = false, bool waitForAvailability = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ProjectFileIdentity.Read(projectPath) is not { } identity) return null;
        var name = identity.MutexName;
        var mutex = new Mutex(false, name, Scope);
        var acquired = false;
        try
        {
            acquired = waitForAvailability ? WaitForGate(mutex, cancellationToken) : TryAcquire(mutex, TimeSpan.Zero);
            cancellationToken.ThrowIfCancellationRequested();
            if (!acquired) throw new ProjectStartupBusyException(projectPath, ReadHolderKind(name));
            if (ProjectFileIdentity.Read(projectPath) != identity)
                throw new ProjectStartupBusyException(projectPath, null, fileChanged: true);

            // The named marker's lifetime identifies the current holder across processes.
            var holderMarker = new Mutex(false, name + (sourceIndex ? IndexMarker : OpenMarker), Scope);
            return new ProjectStartupGate(mutex, holderMarker);
        }
        catch
        {
            try { if (acquired) mutex.ReleaseMutex(); }
            finally { mutex.Dispose(); }
            throw;
        }
    }

    private static bool WaitForGate(Mutex mutex, CancellationToken cancellationToken)
    {
        if (TryAcquire(mutex, TimeSpan.Zero)) return true;
        WaitingForWorkerRead.Value?.Invoke();
        if (!cancellationToken.CanBeCanceled) return TryAcquire(mutex, WaitBudget);

        var started = Stopwatch.GetTimestamp();
        // Single-handle waits preserve named mutex support and thread ownership on Unix.
        while (WaitBudget - Stopwatch.GetElapsedTime(started) is { Ticks: > 0 } remaining)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (TryAcquire(mutex, remaining < CancellationInterval ? remaining : CancellationInterval)) return true;
        }
        return false;
    }

    private static bool TryAcquire(Mutex mutex, TimeSpan elapsed)
    {
        try { return mutex.WaitOne(elapsed); }
        catch (AbandonedMutexException) { return true; }
    }

    private static string? ReadHolderKind(string name)
    {
        try
        {
            if (Mutex.TryOpenExisting(name + IndexMarker, Scope, out var index))
            {
                index.Dispose();
                return "The Source index worker";
            }
            if (Mutex.TryOpenExisting(name + OpenMarker, Scope, out var opener))
            {
                opener.Dispose();
                return "Another Motif project opener";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return null;
    }

    public void Dispose()
    {
        try { _holderMarker.Dispose(); }
        finally
        {
            try { _mutex.ReleaseMutex(); }
            finally { _mutex.Dispose(); }
        }
    }
}
