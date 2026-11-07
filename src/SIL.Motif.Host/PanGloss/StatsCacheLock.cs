using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Coordinates invocation-directory leases and shared Assessment source leases.</summary>
/// <remarks>
/// Two unrelated invocation directories never share a lease. The lease name is the full SHA-256 of the directory path,
/// 24 characters of prefix plus 64 of hex, well inside the 259-character limit of a Windows mutex name and of a Unix
/// file name. Invocation lock files are temporary. Shared source readers coexist until collection takes the exclusive lease.
/// </remarks>
public static class StatsCacheLock
{
    /// <summary>Waits for a retained file's invocation lock until it is free or the token is cancelled.</summary>
    /// <remarks>The holder is another Assessment, a reader, or cleanup of the same directory; none holds it for long
    /// except a running parser, whose directory no reader names until it is published.</remarks>
    public static WorkerMutexOwner AcquireForFile(string path, CancellationToken cancellationToken = default)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (var current = directory; current.Parent is not null; current = current.Parent)
            if (string.Equals(current.Parent.Name, "assessment-runs", StringComparison.OrdinalIgnoreCase))
            {
                directory = current;
                break;
            }
        var owner = string.Equals(directory.Name, "assessment-sources", StringComparison.OrdinalIgnoreCase)
            ? CreateSharedSourceLock(directory.FullName, exclusive: false) : Create(directory.FullName);
        try
        {
            while (!owner.TryAcquire())
            {
                cancellationToken.ThrowIfCancellationRequested();
                cancellationToken.WaitHandle.WaitOne(RetryInterval);
            }
            return owner;
        }
        catch
        {
            owner.Dispose();
            throw;
        }
    }

    /// <summary>How long a waiting reader sleeps between attempts; also the cancellation latency.</summary>
    public static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>Creates a lease for the invocation directory.</summary>
    public static WorkerMutexOwner Create(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return WorkerMutexOwner.ForTransientRoot("Global\\Motif.StatsCache." + digest);
    }

    /// <summary>Creates a shared source reader lease or the exclusive lease used by collection.</summary>
    public static WorkerMutexOwner CreateSharedSourceLock(string directory, bool exclusive)
    {
        var path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return new WorkerMutexOwner("Global\\Motif.AssessmentSources." + digest, true,
            exclusive ? WorkerLockAccess.Exclusive : WorkerLockAccess.Shared);
    }
}
