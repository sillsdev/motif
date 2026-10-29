using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Assess;

/// <summary>Resolves run-specific artifact directories inside the owned worker root.</summary>
/// <remarks>The final directory is left absent so publication can refuse an invocation id that already exists.</remarks>
public sealed class StatsCacheStore : IAssessorCachePathResolver
{
    private const string RootSegment = "assessment-runs";
    private readonly IWorkspaceOwnership _ownership;
    private readonly string? _projectStorePath;

    public StatsCacheStore(IWorkspaceOwnership ownership, string? projectStorePath = null)
    {
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _projectStorePath = projectStorePath is null ? null : Path.GetFullPath(projectStorePath);
    }

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
        if (_projectStorePath is not null) RecordOwner(path, _projectStorePath);
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
                if (!_ownership.IsOwned(directory)) continue;
                var hasCache = File.Exists(cache);
                if (hasCache && ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0 ||
                    File.GetLastWriteTimeUtc(cache) >= nowUtc - maximumAge)) continue;
                if (!hasCache && Directory.GetLastWriteTimeUtc(directory) >= nowUtc - maximumAge) continue;
                using var lease = StatsCacheLock.Create(directory);
                if (!lease.TryAcquire()) continue;
                if (File.Exists(cache))
                {
                    if ((File.GetAttributes(cache) & FileAttributes.ReparsePoint) != 0 ||
                        File.GetLastWriteTimeUtc(cache) >= nowUtc - maximumAge) continue;
                }
                else if (Directory.GetLastWriteTimeUtc(directory) >= nowUtc - maximumAge) continue;
                if (IsReferencedOrUnknown(directory, cache) || !ContainsOnlyOwnedEntries(directory)) continue;
                Directory.Delete(directory, recursive: true);
                File.Delete(OwnerPath(directory));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        foreach (var owner in Directory.EnumerateFiles(root, "*.owner"))
        {
            try
            {
                var directory = owner[..^".owner".Length];
                if (!_ownership.IsOwned(owner) || (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0 ||
                    Directory.Exists(directory) || File.GetLastWriteTimeUtc(owner) >= nowUtc - maximumAge) continue;
                using var lease = StatsCacheLock.Create(directory);
                if (lease.TryAcquire() && !Directory.Exists(directory) &&
                    File.GetLastWriteTimeUtc(owner) < nowUtc - maximumAge) File.Delete(owner);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal void SweepExpired() => SweepExpired(DateTime.UtcNow, MaximumAge());

    private void RecordOwner(string directory, string projectStorePath)
    {
        var owner = OwnerPath(directory);
        RequireOwned(owner);
        try
        {
            using var stream = new FileStream(owner, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream);
            writer.Write(projectStorePath);
        }
        catch (IOException) when (File.Exists(owner))
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if ((File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(File.ReadAllText(owner), projectStorePath, comparison))
                throw new InvalidOperationException("The invocation directory has another project owner.");
        }
    }

    private bool IsReferencedOrUnknown(string directory, string cache)
    {
        var owner = OwnerPath(directory);
        try
        {
            if (!_ownership.IsOwned(owner) || !File.Exists(owner) ||
                (File.GetAttributes(owner) & FileAttributes.ReparsePoint) != 0) return true;
            var storePath = File.ReadAllText(owner);
            if (!Path.IsPathFullyQualified(storePath) || !File.Exists(storePath)) return true;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = storePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA application_id;";
            if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != MotifSchema.ApplicationId)
                return true;
            command.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != MotifSchema.CurrentSchema)
                return true;
            command.CommandText = "SELECT 1 FROM Assessments WHERE InvocationId = $invocation OR CachePath = $cache LIMIT 1;";
            command.Parameters.AddWithValue("$invocation", Path.GetFileName(directory));
            command.Parameters.AddWithValue("$cache", cache);
            return command.ExecuteScalar() is not null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or ArgumentException)
        {
            return true;
        }
    }

    private bool ContainsOnlyOwnedEntries(string directory)
    {
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.TryPop(out var current))
            foreach (var entry in Directory.EnumerateFileSystemEntries(current))
            {
                if (!_ownership.IsOwned(entry)) return false;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        return true;
    }

    private static string OwnerPath(string directory) => directory + ".owner";

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
