using System.Security.Cryptography;
using System.Text;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Coordinates statistics-cache readers, writers and cleanup with a bounded set of lock files.</summary>
public static class StatsCacheLock
{
    /// <summary>Creates a lease for the invocation directory.</summary>
    public static WorkerMutexOwner Create(string directory)
    {
        var path = Path.GetFullPath(directory);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()) path = path.ToUpperInvariant();
        var stripe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)))[..2];
        return WorkerMutexOwner.ForRoot("Global\\Motif.StatsCache." + stripe);
    }
}
