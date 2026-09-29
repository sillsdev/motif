using System;
using System.IO;

namespace SIL.Motif.Host.Store;

/// <summary>Identifies contention on a database creation lock or active-use lease.</summary>
/// <remarks>
/// This type keeps callers from treating unrelated storage failures as lock contention. It is raised only after
/// the operating system confirms another process holds one of Motif's database locks.
/// </remarks>
public sealed class MotifStoreLockException : IOException
{
    /// <summary>Creates a lock-contended failure with the underlying operating-system exception.</summary>
    public MotifStoreLockException(string message, Exception innerException)
        : base(message, innerException) { }
}
