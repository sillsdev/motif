using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Coordinates statistics-cache readers, writers and cleanup with a bounded set of lock files.</summary>
public static class StatsCacheLock
{
    /// <summary>Waits up to five seconds for a retained file's invocation lock.</summary>
    public static WorkerMutexOwner AcquireForFile(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!);
        for (var current = directory; current.Parent is not null; current = current.Parent)
            if (string.Equals(current.Parent.Name, "assessment-runs", StringComparison.OrdinalIgnoreCase))
            {
                directory = current;
                break;
            }
        var owner = Create(directory.FullName);
        if (SpinWait.SpinUntil(owner.TryAcquire, TimeSpan.FromSeconds(5))) return owner;
        owner.Dispose();
        throw new InvalidDataException("The retained Assessment files are busy; try again.");
    }

    /// <summary>Creates a lease for the invocation directory.</summary>
    public static WorkerMutexOwner Create(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var stripe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..2];
        return WorkerMutexOwner.ForRoot("Global\\Motif.StatsCache." + stripe);
    }
}
