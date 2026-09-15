using System;
using System.IO;

namespace SIL.Motif.Host.Store;

/// <summary>Identifies contention on Motif's short-lived database creation ownership lock.</summary>
/// <remarks>
/// This type keeps callers from treating unrelated storage failures as retryable contention. It is
/// raised only after the ownership lock source has confirmed another process holds that lock.
/// </remarks>
public sealed class MotifStoreLockException : IOException
{
    /// <summary>Creates a lock-contended failure with the underlying operating-system exception.</summary>
    public MotifStoreLockException(string message, Exception innerException)
        : base(message, innerException) { }
}
