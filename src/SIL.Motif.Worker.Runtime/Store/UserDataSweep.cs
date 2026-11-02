using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>What one sweep of the per-user data folder removed and what it kept.</summary>
/// <param name="RunsRemoved">Assessment run directories removed.</param>
/// <param name="SourcesRemoved">Retained Assessment source files removed.</param>
/// <param name="BaselinesRemoved">Published Baseline directories removed.</param>
/// <param name="ProjectsForgotten">Known projects whose folder is gone and that were dropped from the machine store.</param>
/// <param name="BytesRemoved">The bytes freed.</param>
/// <param name="BytesKept">The bytes the three families hold afterwards.</param>
/// <param name="UnreadableStores">Live stores whose references could not be read, which blocks the removal of unowned entries.</param>
/// <param name="Failures">Entries that could not be removed, with the reason.</param>
public sealed record UserDataSweepResult(int RunsRemoved, int SourcesRemoved, int BaselinesRemoved,
    int ProjectsForgotten, long BytesRemoved, long BytesKept, int UnreadableStores, IReadOnlyList<string> Failures);

/// <summary>Tunes <see cref="UserDataSweep"/>; the defaults are what the worker uses.</summary>
public sealed record UserDataSweepOptions
{
    /// <summary>The bytes the run, source and Baseline families may hold together: 5 GB.</summary>
    public long BudgetBytes { get; init; } = 5L * 1024 * 1024 * 1024;

    /// <summary>The age below which an unreferenced run is left alone, because a writer may not have recorded it yet.</summary>
    public TimeSpan MinimumAge { get; init; } = TimeSpan.FromDays(7);

    /// <summary>The age below which no entry is removed, even to meet the budget.</summary>
    public TimeSpan BudgetMinimumAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How long a Known project whose folder is gone stays registered.</summary>
    public TimeSpan ProjectGrace { get; init; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Removes what no live project store references from the per-user data folder: Assessment runs, retained
/// Assessment sources and published Baselines, including those of projects that no longer exist.
/// </summary>
/// <remarks>
/// A store is live when its Known project still has a Motif file next to a FieldWorks project, or when a run
/// directory's owner marker names it and it exists. An entry is referenced when any live store names it, by id,
/// digest or path, in the tables that hold evidence, and a referenced entry is never removed. An unreferenced run
/// goes once it is older than <see cref="UserDataSweepOptions.MinimumAge"/>; an unreferenced Baseline or source goes
/// only when the families exceed their budget, oldest first, once a day old. Nothing unowned goes unless every Known
/// project is accounted for: its store was read, it has no store in a folder that exists, or its folder has been
/// missing since a marker written by an earlier sweep for the grace. A store that cannot be read, or a project whose
/// folder is missing (an absent drive), keeps every unowned entry. A run whose owner store is not found is kept
/// until it is older than the grace; an unparseable marker keeps the run.
/// </remarks>
public sealed class UserDataSweep
{
    private static readonly string[] Families = ["assessment-runs", "assessment-sources", "baselines"];

    // Every column that can name a run, source or Baseline; absent tables and columns in older stores are skipped.
    private static readonly (string Table, string Column)[] ReferenceColumns =
    [
        ("Assessments", "InvocationId"), ("Assessments", "CachePath"), ("Assessments", "ScopeJson"),
        ("AssessmentInvocations", "InvocationId"), ("AssessmentInvocations", "EvidenceJson"),
        ("RetainedInvocations", "ArtifactInvocationId"), ("RetainedInvocations", "BaselineFwDataPath"),
        ("Baselines", "RootDirectory"), ("Baselines", "FwDataPath"), ("Baselines", "BundleDigest"),
        ("ParsimonyBundles", "BaselineRootDirectory"), ("ParsimonyBundles", "EvidenceDirectory"),
        ("ParsimonyBundles", "BaselineDigest"), ("ParsimonyBuildClaims", "BaselineRootDirectory"),
        ("Jobs", "InputJson"), ("Jobs", "ResultJson"), ("Reports", "ReportJson"), ("Reports", "EvidenceJson"),
        ("Proposals", "DraftJson"), ("Proposals", "AnchorJson"), ("ProposalRevisions", "ProposalJson"),
        ("Receipts", "ReceiptJson"), ("AppliedIndex", "RecordJson"),
    ];

    private readonly string _root;
    private readonly KnownProjectRegistry _projects;
    private readonly TimeProvider _clock;
    private readonly UserDataSweepOptions _options;

    /// <summary>Creates a sweep of <paramref name="root"/>, the folder that holds the machine store.</summary>
    public UserDataSweep(string root, KnownProjectRegistry projects, TimeProvider? clock = null,
        UserDataSweepOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = Path.GetFullPath(root);
        _projects = projects ?? throw new ArgumentNullException(nameof(projects));
        _clock = clock ?? TimeProvider.System;
        _options = options ?? new UserDataSweepOptions();
    }

    /// <summary>Runs one sweep, checking <paramref name="cancellationToken"/> between entries.</summary>
    public UserDataSweepResult Sweep(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var failures = new List<string>();
        var liveStores = new Dictionary<string, bool>(PathComparer);
        var forgotten = 0;
        var unaccounted = 0;
        foreach (var project in _projects.List())
        {
            var store = Path.ChangeExtension(project.FullFwDataPath, ".motif.db");
            var marker = MissingMarker(project.WorkspaceKey);
            if (File.Exists(store) || Directory.Exists(Path.GetDirectoryName(project.FullFwDataPath)))
            {
                if (File.Exists(marker)) File.Delete(marker);
                if (File.Exists(store)) liveStores[Path.GetFullPath(store)] = false;
                continue;
            }
            // The grace runs from the first sweep that saw the folder missing, not from the project's last use.
            if (!File.Exists(marker))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
                File.WriteAllText(marker, project.FullFwDataPath);
                File.SetLastWriteTimeUtc(marker, now);
            }
            if (now - File.GetLastWriteTimeUtc(marker) >= _options.ProjectGrace)
            {
                _projects.Forget(project.WorkspaceKey);
                File.Delete(marker);
                forgotten++;
            }
            else unaccounted++;
        }

        var entries = new List<Entry>();
        foreach (var family in Families)
        {
            var directory = Path.Combine(_root, family);
            if (!Directory.Exists(directory)) continue;
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsSidecar(path)) continue;
                try { entries.Add(Describe(family, path, now)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failures.Add(path + ": " + exception.Message);
                }
            }
        }

        foreach (var entry in entries)
            if (entry.OwnerStore is { } owner && TryFullPath(owner, out var ownerPath) && File.Exists(ownerPath))
                liveStores.TryAdd(ownerPath, false);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var unreadable = unaccounted;
        foreach (var store in liveStores.Keys.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReadTokens(store, tokens)) continue;
            liveStores[store] = true;
            unreadable++;
        }

        var removable = new List<Entry>();
        foreach (var entry in entries)
        {
            if (tokens.Contains(entry.Key) || entry.MarkerUnreadable) continue;
            if (entry.OwnerStore is { } owner)
            {
                if (!TryFullPath(owner, out var ownerPath)) continue;
                if (liveStores.TryGetValue(ownerPath, out var ownerUnreadable))
                {
                    if (ownerUnreadable) continue;
                }
                else if (unreadable > 0 || now - entry.LastWriteUtc < _options.ProjectGrace) continue;
            }
            else if (unreadable > 0) continue;
            removable.Add(entry);
        }

        long removedBytes = 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var remaining = entries.Sum(entry => entry.Bytes);
        var ordered = removable.OrderBy(entry => entry.LastWriteUtc).ToList();
        var removedSet = new HashSet<Entry>();
        // Only runs age out by themselves; a Baseline or source is removed under the budget alone.
        foreach (var entry in ordered.Where(entry => entry.Family == "assessment-runs" &&
                     now - entry.LastWriteUtc >= _options.MinimumAge))
            Remove(entry);
        foreach (var entry in ordered.Where(entry => !removedSet.Contains(entry) &&
                     now - entry.LastWriteUtc >= _options.BudgetMinimumAge))
        {
            if (remaining <= _options.BudgetBytes) break;
            Remove(entry);
        }

        return new UserDataSweepResult(counts.GetValueOrDefault("assessment-runs"),
            counts.GetValueOrDefault("assessment-sources"), counts.GetValueOrDefault("baselines"), forgotten,
            removedBytes, remaining, unreadable, failures);

        void Remove(Entry entry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Delete(entry.Path);
                foreach (var sidecar in new[] { entry.Path + ".owner", entry.Path + ".prune" })
                    if (File.Exists(sidecar)) File.Delete(sidecar);
                removedBytes += entry.Bytes;
                remaining -= entry.Bytes;
                counts[entry.Family] = counts.GetValueOrDefault(entry.Family) + 1;
                removedSet.Add(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(entry.Path + ": " + exception.Message);
            }
        }
    }

    private string MissingMarker(string workspaceKey) => Path.Combine(_root, "missing-projects",
        string.Concat(workspaceKey.Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '_')));

    private static bool TryFullPath(string path, out string full)
    {
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            full = path;
            return false;
        }
    }

    private static bool IsSidecar(string path) => path.EndsWith(".owner", StringComparison.Ordinal) ||
        path.EndsWith(".prune", StringComparison.Ordinal);

    private static Entry Describe(string family, string path, DateTime now)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A link is never followed.");
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var name = Path.GetFileName(path);
        var key = isDirectory ? name : name.Split('.')[0];
        long bytes = 0;
        var newest = isDirectory ? Directory.GetLastWriteTimeUtc(path) : File.GetLastWriteTimeUtc(path);
        if (isDirectory)
        {
            foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                bytes += file.Length;
                if (file.LastWriteTimeUtc > newest) newest = file.LastWriteTimeUtc;
            }
        }
        else bytes = new FileInfo(path).Length;
        string? owner = null;
        var markerUnreadable = false;
        var marker = path + ".owner";
        if (family == "assessment-runs" && File.Exists(marker))
            try { owner = File.ReadAllText(marker).Trim(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { markerUnreadable = true; }
        if (newest > now) newest = now;
        return new Entry(family, path, key, bytes, newest, string.IsNullOrEmpty(owner) ? null : owner, markerUnreadable);
    }

    // Bottom-up delete that stops at the first link.
    private static void Delete(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
            return;
        }
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A link inside the entry is refused.");
            Delete(child);
        }
        Directory.Delete(path);
    }

    // Reads every id, digest and path the store names; false when the store cannot be read as a Motif store.
    private static bool ReadTokens(string storePath, HashSet<string> tokens)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = storePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5,
            }.ToString());
            connection.Open();
            if (Pragma(connection, "application_id") != MotifSchema.ApplicationId ||
                Pragma(connection, "user_version") > MotifSchema.CurrentSchema)
                return false;
            foreach (var (table, column) in ReferenceColumns)
            {
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = $"SELECT {column} FROM {table} WHERE {column} IS NOT NULL;";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                        AddTokens(reader.GetValue(0) is byte[] bytes ? System.Text.Encoding.UTF8.GetString(bytes)
                            : Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!, tokens);
                }
                catch (SqliteException exception) when (exception.SqliteErrorCode == 1)
                {
                    // An older store may lack the table or column; it then cannot name a file through it.
                }
            }
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    private static long Pragma(SqliteConnection connection, string name)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {name};";
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AddTokens(string text, HashSet<string> tokens)
    {
        var start = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var inToken = index < text.Length && (char.IsAsciiLetterOrDigit(text[index]) || text[index] is '-' or '_');
            if (inToken)
            {
                if (start < 0) start = index;
                continue;
            }
            if (start >= 0 && index - start >= 8) tokens.Add(text[start..index]);
            start = -1;
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private sealed record Entry(string Family, string Path, string Key, long Bytes, DateTime LastWriteUtc,
        string? OwnerStore, bool MarkerUnreadable);
}
