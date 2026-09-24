namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// The machine-wide mutex SIL.WritingSystems takes around its SLDR cache, and recovery from a holder that died
/// holding it.
/// </summary>
public static class SldrCacheMutex
{
    /// <summary>The name SIL.WritingSystems gives the mutex on Windows.</summary>
    public const string Name = "SldrCache";

    /// <summary>
    /// Clears the abandoned state a killed holder leaves behind, so the next SLDR call does not throw.
    /// </summary>
    /// <remarks>
    /// SIL.WritingSystems waits on this mutex without handling <see cref="AbandonedMutexException"/>, so a
    /// process killed while holding it breaks every writing-system load that follows, in any process on the
    /// machine, until something takes it and releases it. Taking it here is that something. A holder that is
    /// still alive keeps it: this does not wait.
    /// </remarks>
    public static void ReclaimIfAbandoned() => ReclaimIfAbandoned(Name);

    /// <summary>Clears an abandoned named mutex, for a caller that has its exact name.</summary>
    public static void ReclaimIfAbandoned(string name)
    {
        if (!OperatingSystem.IsWindows()) return;

        using var mutex = new Mutex(false, name);
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            acquired = true;
        }
        if (acquired) mutex.ReleaseMutex();
    }
}
