using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Installation;

/// <summary>Coordinates package updates with work that uses the installed application files.</summary>
public static class MotifUpdateGate
{
    private const string MutexName = "SIL.Motif.UpdateActivity";

    /// <summary>Acquires the activity lease, or returns <see langword="null"/> when work is active.</summary>
    public static IDisposable? TryAcquire()
    {
        return TryAcquire(MutexName);
    }

    internal static IDisposable? TryAcquire(string mutexName)
    {
        var owner = new WorkerMutexOwner(mutexName);
        if (owner.TryAcquire())
            return owner;

        owner.Dispose();
        return null;
    }
}
