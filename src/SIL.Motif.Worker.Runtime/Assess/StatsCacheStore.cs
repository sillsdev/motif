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
        if (_projectStorePath is not null) RecordOwner(path, _projectStorePath);
        return path;
    }

    private void RecordOwner(string directory, string projectStorePath)
    {
        var owner = OwnerPath(directory);
        RequireOwned(owner);
        if (Directory.Exists(directory) && !File.Exists(owner))
            throw new InvalidOperationException("The existing invocation directory has no project owner.");
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

    /// <summary>Reclaims this project's unreferenced runs and reports directories of unproven ownership.</summary>
    public (int Pruned, int UnownedCount, long UnownedBytes, bool SizeComplete, string Parent) Reclaim(
        MotifDatabase database, ProjectParseLease lease, IReadOnlyCollection<string> invocations)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(lease);
        ArgumentNullException.ThrowIfNull(invocations);
        if (!SamePath(database.FullPath, _projectStorePath))
            throw new InvalidOperationException("The Assessment directory owner does not match the project store.");
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
            lease.EnsureHeld(connection, transaction);
        var root = Path.Combine(_ownership.WorkerRoot, RootSegment);
        RequireOwned(root);
        if (!Directory.Exists(root)) return (0, 0, 0, true, root);
        var candidates = invocations.ToHashSet(StringComparer.Ordinal);
        var pruned = 0;
        var unowned = 0;
        long unownedBytes = 0;
        var sizeComplete = true;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            try
            {
                var owner = OwnerPath(directory);
                if (!_ownership.IsOwned(directory) || !MarkerMatchesProject(owner))
                {
                    unowned++;
                    if (!TryMeasure(directory, out var bytes)) sizeComplete = false;
                    unownedBytes = checked(unownedBytes + bytes);
                    continue;
                }
                var cache = Path.Combine(directory, "stats.sqlite");
                var pending = directory + ".prune";
                var candidate = candidates.Contains(Path.GetFileName(directory)) ||
                    candidates.Any(value => SamePath(value, directory));
                if (candidate) MarkPending(pending);
                var retry = IsPending(pending);
                using var cacheLock = StatsCacheLock.Create(directory);
                if (!cacheLock.TryAcquire()) continue;
                if (!MarkerMatchesProject(owner)) continue;
                var state = StoreReferenceStatus(database.FullPath, directory);
                if (state == ReferenceState.Referenced && retry) File.Delete(pending);
                var expired = IsOlderThan(directory, cache, DateTime.UtcNow - MaximumAge());
                if (state != ReferenceState.Unreferenced || (!candidate && !retry && !expired) ||
                    !ContainsOnlyOwnedEntries(directory)) continue;
                Directory.Delete(directory, recursive: true);
                try { File.Delete(owner); File.Delete(pending); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                pruned++;
            }
            catch (IOException) { sizeComplete = false; }
            catch (UnauthorizedAccessException) { sizeComplete = false; }
        }
        foreach (var owner in Directory.EnumerateFiles(root, "*.owner"))
        {
            try
            {
                var directory = owner[..^".owner".Length];
                if (Directory.Exists(directory) || !MarkerMatchesProject(owner)) continue;
                using var cacheLock = StatsCacheLock.Create(directory);
                if (!cacheLock.TryAcquire() || Directory.Exists(directory) || !MarkerMatchesProject(owner)) continue;
                File.Delete(owner);
                var pending = directory + ".prune";
                if (IsPending(pending)) File.Delete(pending);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return (pruned, unowned, unownedBytes, sizeComplete, root);
    }

    private bool TryMeasure(string directory, out long bytes)
    {
        bytes = 0;
        try
        {
            var pending = new Stack<string>();
            pending.Push(directory);
            while (pending.TryPop(out var current))
            {
                if (!_ownership.IsOwned(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
                foreach (var entry in Directory.EnumerateFileSystemEntries(current))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) return false;
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                    else bytes = checked(bytes + new FileInfo(entry).Length);
                }
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return false;
        }
    }

    private bool MarkerMatchesProject(string owner)
    {
        try
        {
            return _ownership.IsOwned(owner) && File.Exists(owner) &&
                (File.GetAttributes(owner) & FileAttributes.ReparsePoint) == 0 &&
                SamePath(File.ReadAllText(owner), _projectStorePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool IsPending(string pending) => _ownership.IsOwned(pending) && File.Exists(pending) &&
        (File.GetAttributes(pending) & FileAttributes.ReparsePoint) == 0;

    private void MarkPending(string pending)
    {
        if (!_ownership.IsOwned(pending)) return;
        try { using var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.Read); }
        catch (IOException) when (File.Exists(pending)) { }
    }

    private static bool IsOlderThan(string directory, string cache, DateTime cutoff) =>
        Directory.GetLastWriteTimeUtc(directory) < cutoff &&
        (!File.Exists(cache) || File.GetLastWriteTimeUtc(cache) < cutoff);

    private static bool SamePath(string? first, string? second) => first is not null && second is not null &&
        string.Equals(first, second, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private enum ReferenceState { Unknown, Referenced, Unreferenced }

    private static ReferenceState StoreReferenceStatus(string storePath, string directory)
    {
        try
        {
            if (!Path.IsPathFullyQualified(storePath) || !File.Exists(storePath)) return ReferenceState.Unknown;
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = storePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA application_id;";
            if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != MotifSchema.ApplicationId)
                return ReferenceState.Unknown;
            command.CommandText = "PRAGMA user_version;";
            if (Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != MotifSchema.CurrentSchema)
                return ReferenceState.Unknown;
            command.CommandText = """
                SELECT 1 FROM Assessments WHERE InvocationId = $invocation
                UNION ALL SELECT 1 FROM RetainedInvocations WHERE ArtifactInvocationId = $invocation
                UNION ALL SELECT 1 FROM AssessmentInvocations
                    WHERE json_extract(EvidenceJson, '$.StoppedWordRetry.RetryInvocationId') = $invocation LIMIT 1;
                """;
            command.Parameters.AddWithValue("$invocation", Path.GetFileName(directory));
            if (command.ExecuteScalar() is not null) return ReferenceState.Referenced;
            command.CommandText = """
                SELECT CachePath FROM Assessments WHERE CachePath IS NOT NULL
                UNION ALL SELECT json_extract(EvidenceJson, '$.SourcePath') FROM AssessmentInvocations
                UNION ALL SELECT json_extract(EvidenceJson, '$.WordsPath') FROM AssessmentInvocations
                UNION ALL SELECT json_extract(EvidenceJson, '$.TsvPath') FROM AssessmentInvocations
                UNION ALL SELECT json_extract(EvidenceJson, '$.StandardErrorPath') FROM AssessmentInvocations
                UNION ALL SELECT json_extract(EvidenceJson, '$.AnalysesPath') FROM AssessmentInvocations
                UNION ALL SELECT BaselineFwDataPath FROM RetainedInvocations;
                """;
            using var reader = command.ExecuteReader();
            var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            while (reader.Read())
            {
                if (reader.IsDBNull(0)) continue;
                var path = reader.GetString(0);
                if (string.IsNullOrWhiteSpace(path)) return ReferenceState.Unknown;
                if (Path.GetFullPath(path).StartsWith(prefix, comparison)) return ReferenceState.Referenced;
            }
            return ReferenceState.Unreferenced;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException or ArgumentException)
        {
            return ReferenceState.Unknown;
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
