using System;
using System.Threading;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands;

/// <summary>Runs a step that another process's SQLite lock may refuse, waiting a bounded time for the lock to clear.</summary>
internal static class BusyRetry
{
    private static readonly int[] DefaultBackoffMilliseconds = [200, 600, 1800];

    /// <summary>The waits between attempts; a test sets it to a short list and restores it.</summary>
    internal static int[] BackoffMilliseconds { get; set; } = DefaultBackoffMilliseconds;

    /// <summary>Runs <paramref name="work"/>, retrying only when a BUSY or LOCKED is in the exception chain.</summary>
    internal static void Run(Action work)
    {
        var backoff = BackoffMilliseconds;
        for (var attempt = 0; ; attempt++)
        {
            try { work(); return; }
            catch (Exception exception) when (attempt < backoff.Length && SqliteConnections.IsBusyInChain(exception))
            {
                Thread.Sleep(backoff[attempt]);
            }
        }
    }
}
