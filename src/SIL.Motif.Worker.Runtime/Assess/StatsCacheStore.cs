using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Assess;

/// <summary>Resolves run-specific artifact directories inside the owned worker root.</summary>
/// <remarks>The final directory is left absent so publication can refuse an invocation id that already exists.</remarks>
public sealed class StatsCacheStore : IAssessorCachePathResolver
{
    private const string RootSegment = "assessment-runs";
    private readonly IWorkspaceOwnership _ownership;

    public StatsCacheStore(IWorkspaceOwnership ownership) =>
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));

    /// <inheritdoc />
    public string DirectoryFor(string invocationId)
    {
        if (string.IsNullOrWhiteSpace(invocationId) ||
            invocationId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_') ||
            IsReservedName(invocationId))
            throw new ArgumentException("An invocation id must be a safe ASCII letter, digit, hyphen or underscore segment.",
                nameof(invocationId));

        var path = Path.Combine(_ownership.WorkerRoot, RootSegment, invocationId);
        RequireOwned(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RequireOwned(path);
        try { SweepExpired(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return path;
    }

    internal void SweepExpired(DateTime nowUtc, TimeSpan maximumAge)
    {
        var root = Path.Combine(_ownership.WorkerRoot, RootSegment);
        RequireOwned(root);
        if (!Directory.Exists(root)) return;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var cache = Path.Combine(directory, "stats.sqlite");
            try
            {
                if (!_ownership.IsOwned(directory) || !File.Exists(cache) ||
                    (File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0 ||
                    File.GetLastWriteTimeUtc(cache) >= nowUtc - maximumAge) continue;
                using var lease = StatsCacheLock.Create(directory);
                if (!lease.TryAcquire() || !File.Exists(cache) ||
                    File.GetLastWriteTimeUtc(cache) >= nowUtc - maximumAge) continue;
                File.Delete(cache);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal void SweepExpired() => SweepExpired(DateTime.UtcNow, MaximumAge());

    private static TimeSpan MaximumAge() =>
        int.TryParse(Environment.GetEnvironmentVariable("MOTIF_STATS_CACHE_MAX_AGE_DAYS"), out var days) && days is > 0 and <= 3650
            ? TimeSpan.FromDays(days) : TimeSpan.FromDays(30);

    private void RequireOwned(string path)
    {
        if (!_ownership.IsOwned(path))
            throw new InvalidOperationException(
                "The Assessment run directory is refused: it resolves outside the worker-owned root or through a reparse point.");
    }

    private static bool IsReservedName(string value) => value.ToUpperInvariant() is
        "CON" or "PRN" or "AUX" or "NUL" or
        "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9" or
        "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9";
}
