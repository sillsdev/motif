using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Backs up and restores a project's Motif store and the files Motif owns beside it.
/// </summary>
/// <remarks>
/// <para>
/// A backup is a folder: the store copied with SQLite's online backup while readers and writers carry on, the
/// project's <c>.motif.toml</c>, the store's upgrade report and its Trial evidence folder when they exist, and
/// <c>motif-backup.json</c>, the receipt with each file's size and sha256. The FieldWorks project file is not
/// copied: FieldWorks backs that up itself, and the receipt says so.
/// </para>
/// <para>
/// A restore checks the receipt, the schema and the project (by LibLCM project GUID, else by project identity;
/// <c>--force</c> overrides), then, under the store's exclusive lease, stages every file beside its target and checks
/// the staged copy's sha256, then checkpoints the live store's WAL into its main file (refusing, with nothing changed, when that cannot finish). Nothing is deleted: the store, its <c>-wal</c>, <c>-shm</c> and <c>-journal</c> and every
/// other owned file the restore replaces or the backup lacks (a trial export, a config, an upgrade report) are
/// moved into <c>&lt;stem&gt;.motif-backups/pre-restore-&lt;time&gt;/moved</c>, and the response lists each one. That
/// move is the one copy of the old store: the raw files, which together reproduce it exactly, so no online backup
/// doubles the bytes. Every
/// refusal leaves the store as it was, pinned by `AFileThatDoesNotMatchItsShaIsRefusedAndTheStoreIsLeftAsItWas`.
/// </para>
/// <para>
/// Crash states, in order. (1) Before any move: the old project is untouched; leftover <c>*.restore-*</c> staged files
/// are deleted by the next restore. (2) Other owned files moved and swapped, store not yet moved: the old store is live
/// beside the new config and trial files, a mixed state a rerun of the same restore repairs. (3) The sidecars moved first, the
/// store not yet: the old store is live and holds every committed transaction, because the restore checkpointed its WAL
/// before any move; no orphan <c>-wal</c> is at the path, and a rerun completes the restore into the same pre-restore
/// folder (marked <c>restore-incomplete</c> until it finishes). (3b) The store moved too, the staged store not yet renamed: no store and no
/// sidecar are at the path, the old files are all in <c>moved</c> and the new store is staged; Motif sees no store,
/// never a half or mixed one, and a rerun completes the restore. (4) The staged store renamed: the new store is live, complete. A failure the process
/// survives (an <see cref="IOException"/>) moves everything back instead, and names the pre-restore folder.
/// </para>
/// </remarks>
public static partial class StoreBackupCommand
{
    /// <summary>The receipt's file name inside a backup folder.</summary>
    public const string ReceiptName = "motif-backup.json";

    private const string IncompleteMarker = "restore-incomplete";
    private const string StoreName = "store.sqlite";
    private const string ConfigName = "Config.motif.toml";
    private const string UpgradeReportName = "store.upgrade.json";
    private const string TrialExportsPrefix = "trial-exports/";
    private const string FwDataNote =
        "The FieldWorks project file (.fwdata) is not part of this backup. Back it up with FieldWorks' own store backup.";

    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Writes a consistent copy of the project's store and owned files into a new folder.</summary>
    public static CommandOutcome<StoreBackupResponse> Backup(StoreBackupRequest request) => Backup(request, null);

    internal static CommandOutcome<StoreBackupResponse> Backup(StoreBackupRequest request, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(request);
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<StoreBackupResponse>.Refused(located.Refusal!);
        var project = located.Value!;
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        if (!File.Exists(storePath))
            return Refuse<StoreBackupResponse>(RefusalCodes.StoreBackupNoStore, FailureReason.NotFound,
                $"There is no Motif store, '{Path.GetFileName(storePath)}', beside this project to back up.", project, storePath);

        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var destination = string.IsNullOrWhiteSpace(request.To)
            ? Path.Combine(Path.GetDirectoryName(storePath)!,
                Path.GetFileNameWithoutExtension(project.FullFwDataPath) + ".motif-backup-" + Stamp(now))
            : Path.GetFullPath(request.To);
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            return Refuse<StoreBackupResponse>(RefusalCodes.StoreBackupDestinationExists, FailureReason.InvalidArgument,
                $"'{destination}' already exists and is not empty. Name a new folder with --to, or leave it off for a timestamped one.",
                project, storePath, ("destination", destination));

        if (OwnedFolderHolding(destination, storePath, project) is { } owned)
            return Refuse<StoreBackupResponse>(RefusalCodes.StoreBackupDestinationOwned, FailureReason.InvalidArgument,
                $"'{destination}' is inside '{owned}', which Motif owns, so a backup written there would be backed up into itself. " +
                "Name a folder outside the store's own files with --to, or leave it off for a timestamped one.",
                project, storePath, ("destination", destination), ("ownedPath", owned));

        var created = !Directory.Exists(destination);
        try
        {
            return CommandOutcome<StoreBackupResponse>.Success(
                WriteBackup(project, storePath, destination, now, file => MotifDatabase.BackUpExisting(storePath, file)));
        }
        catch (Exception exception) when (exception is MotifStoreLockException or InvalidDataException or IOException
            or UnauthorizedAccessException)
        {
            Discard(destination, created);
            return Failed<StoreBackupResponse>(exception, project, storePath, "back up");
        }
    }

    /// <summary>Puts a backup's store and owned files back, after saving the store they replace.</summary>
    public static CommandOutcome<StoreRestoreResponse> Restore(StoreRestoreRequest request) => Restore(request, null);

    internal static CommandOutcome<StoreRestoreResponse> Restore(StoreRestoreRequest request, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BackupPath))
            return CommandOutcome<StoreRestoreResponse>.Refused(new Refusal(RefusalCodes.StoreRestoreInvalid,
                FailureReason.InvalidArgument, "A backup folder is required.", new Dictionary<string, string>()));
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<StoreRestoreResponse>.Refused(located.Refusal!);
        var project = located.Value!;
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);

        var read = ReadReceipt(request.BackupPath, project, storePath);
        if (read.Refusal is { } refusal) return CommandOutcome<StoreRestoreResponse>.Refused(refusal);
        var (directory, receipt, targets) = read.Value;

        if (receipt.SchemaVersion > MotifSchema.CurrentSchema)
            return NewerSchema(receipt.SchemaVersion, project, storePath);
        if (!request.Force && !SameProject(receipt, project))
            return Refuse<StoreRestoreResponse>(RefusalCodes.StoreRestoreOtherProject, FailureReason.Refused,
                $"This backup is of project '{receipt.ProjectIdentity}'" +
                (receipt.ProjectGuid is null ? "" : $" ({receipt.ProjectGuid})") +
                $" (store of '{receipt.ProjectPath}'), not of '{project.FullFwDataPath}'. " +
                "Restore it for that project, or pass --force to restore it here anyway; nothing was changed.", project, storePath,
                (RefusalFactNames.RegisteredProjectPath, receipt.ProjectPath), ("backupProjectIdentity", receipt.ProjectIdentity),
                ("backupProjectGuid", receipt.ProjectGuid ?? ""));

        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var staging = Guid.NewGuid().ToString("N");
        var staged = new List<StagedFile>();
        var progress = new RestoreProgress();
        try
        {
            return MotifDatabase.WithExclusiveStoreFiles(storePath, null, () =>
                RestoreLocked(project, storePath, directory, receipt, targets, staging, staged, now, progress));
        }
        catch (Exception exception) when (exception is MotifStoreLockException or InvalidDataException or IOException
            or UnauthorizedAccessException)
        {
            foreach (var file in staged) file.Discard();
            return Failed<StoreRestoreResponse>(exception, project, storePath, "restore", progress.Directory);
        }
    }

    private static CommandOutcome<StoreRestoreResponse> RestoreLocked(ProjectLocator project, string storePath,
        string directory, StoreBackupResponse receipt, List<Target> targets, string staging, List<StagedFile> staged,
        DateTimeOffset now, RestoreProgress progress)
    {
        RemoveLeftoverStagedFiles(project, storePath);
        foreach (var target in targets)
        {
            var stagedPath = target.Destination + ".restore-" + staging;
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            var file = new StagedFile(target, stagedPath);
            staged.Add(file);
            CopyShared(Path.Combine(directory, target.Entry.Name.Replace('/', Path.DirectorySeparatorChar)), stagedPath);
            var (bytes, sha) = Measure(stagedPath);
            if (bytes != target.Entry.Bytes || !string.Equals(sha, target.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var each in staged) each.Discard();
                return Refuse<StoreRestoreResponse>(RefusalCodes.StoreRestoreChecksumMismatch, FailureReason.Refused,
                    $"'{target.Entry.Name}' in the backup does not match its receipt (expected sha256 " +
                    $"{target.Entry.Sha256}, found {sha}). The backup is damaged or was edited; nothing was changed.",
                    project, storePath, ("file", target.Entry.Name), ("expectedSha256", target.Entry.Sha256), ("actualSha256", sha));
            }
        }
        Step("staged");

        var stagedStore = staged.First(file => file.Target.Entry.Role == "store");
        StoreFileFacts facts;
        try
        {
            facts = MotifDatabase.ReadStoreFileFacts(stagedStore.StagedPath);
        }
        catch (InvalidDataException exception)
        {
            foreach (var each in staged) each.Discard();
            return Refuse<StoreRestoreResponse>(RefusalCodes.StoreRestoreInvalid, FailureReason.InvalidArgument,
                "The backup's store is not a readable Motif store: " + exception.Message, project, storePath);
        }
        if (facts.SchemaVersion > MotifSchema.CurrentSchema)
        {
            foreach (var each in staged) each.Discard();
            return NewerSchema(facts.SchemaVersion, project, storePath);
        }

        // Fold the WAL into the main file first; then the sidecars hold nothing, whichever order they move in.
        if (File.Exists(storePath))
            try { MotifDatabase.CheckpointHeldExclusively(storePath); }
            catch (InvalidDataException) { }
        Step("checkpointed");

        var backupsRoot = Path.Combine(Path.GetDirectoryName(storePath)!,
            Path.GetFileNameWithoutExtension(project.FullFwDataPath) + ".motif-backups");
        // A restore that died partway left a marker; the rerun fills that folder.
        var preRestore = Directory.Exists(backupsRoot)
            ? Directory.GetDirectories(backupsRoot, "pre-restore-*")
                .Where(folder => File.Exists(Path.Combine(folder, IncompleteMarker))).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
        if (preRestore is null)
        {
            preRestore = Path.Combine(backupsRoot, "pre-restore-" + Stamp(now));
            for (var n = 2; Directory.Exists(preRestore) || File.Exists(preRestore); n++)
                preRestore = Path.Combine(backupsRoot, "pre-restore-" + Stamp(now) + "-" + n);
        }
        Directory.CreateDirectory(preRestore);
        File.WriteAllText(Path.Combine(preRestore, IncompleteMarker), "");
        progress.Directory = preRestore;
        var movedRoot = Path.Combine(preRestore, "moved");
        var sidecars = new[] { "-wal", "-shm", "-journal" };

        var moved = new List<ProjectQuarantinedFile>();
        var swapped = new List<StagedFile>();
        try
        {
            // 1. Every other owned file the restore replaces or the backup lacks moves aside whole; nothing is overwritten.
            var restoredDestinations = new HashSet<string>(targets.Select(target => target.Destination), FileSystemComparer);
            var others = new List<(string Original, string Relative)>
            {
                (Path.ChangeExtension(project.FullFwDataPath, ".motif.toml"), ConfigName),
                (storePath + ".upgrade.json", UpgradeReportName),
            };
            var trialRoot = MotifDatabase.TrialExportDirectory(storePath);
            if (Directory.Exists(trialRoot))
                foreach (var source in Directory.EnumerateFiles(trialRoot, "*", SearchOption.AllDirectories)
                    .Where(path => !IsStagedName(path)).Order(StringComparer.Ordinal))
                    others.Add((source, TrialExportsPrefix + Path.GetRelativePath(trialRoot, source).Replace('\\', '/')));
            foreach (var (original, relative) in others)
                if (File.Exists(original))
                    MoveAside(original, Path.Combine(movedRoot, relative.Replace('/', Path.DirectorySeparatorChar)),
                        restoredDestinations.Contains(original) ? "replaced" : "not-in-backup", moved, progress, preRestore);
            Step("owned-quarantined");

            foreach (var file in staged.Where(file => file.Target.Entry.Role != "store"))
            {
                file.SwapIn();
                swapped.Add(file);
            }
            Step("owned-swapped");

            // 2. Sidecars first, so none is left at the live path beside a store created there.
            foreach (var suffix in sidecars)
                if (File.Exists(storePath + suffix))
                    MoveAside(storePath + suffix, Path.Combine(movedRoot, StoreName + suffix), "replaced", moved, progress, preRestore);
            Step("sidecars-moved");
            if (File.Exists(storePath))
                MoveAside(storePath, Path.Combine(movedRoot, StoreName), "replaced", moved, progress, preRestore);
            Step("old-store-moved");

            stagedStore.SwapIn();
            swapped.Add(stagedStore);
            Step("store-swapped");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RollBack(swapped, moved);
            throw;
        }

        try { File.Delete(Path.Combine(preRestore, IncompleteMarker)); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        // Removed only when empty: a reused folder may hold files an earlier attempt moved.
        try { Directory.Delete(preRestore, recursive: false); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        var restored = targets.Select(target => new StoreBackupFile(target.Entry.Role, target.Entry.Name,
            target.Destination, target.Entry.Bytes, target.Entry.Sha256)).ToList();
        return CommandOutcome<StoreRestoreResponse>.Success(new StoreRestoreResponse(
            storePath, directory, facts.SchemaVersion, now, restored,
            Directory.Exists(preRestore) ? preRestore : null, moved));
    }

    private static void MoveAside(string original, string destination, string reason, List<ProjectQuarantinedFile> moved,
        RestoreProgress progress, string preRestore)
    {
        progress.Directory = preRestore;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        // A name taken in a reused folder holds an earlier attempt's file; keep both.
        for (var n = 2; File.Exists(destination) && n < 1000; n++) destination = destination.Split(".again-")[0] + ".again-" + n;
        File.Move(original, destination);
        moved.Add(new ProjectQuarantinedFile(original, destination, reason));
    }

    /// <summary>Undoes a survivable failure: removes what was swapped in, moves back what was set aside.</summary>
    private static void RollBack(List<StagedFile> swapped, List<ProjectQuarantinedFile> moved)
    {
        foreach (var file in swapped)
            try { File.Delete(file.Target.Destination); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        for (var index = moved.Count - 1; index >= 0; index--)
            try { File.Move(moved[index].MovedTo, moved[index].OriginalPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string? OwnedFolderHolding(string destination, string storePath, ProjectLocator project)
    {
        var windows = OperatingSystem.IsWindows();
        var parent = Path.GetDirectoryName(storePath)!;
        var real = ResolveRealPath(destination, windows);
        foreach (var owned in OwnedPaths(storePath, parent, Path.GetFileNameWithoutExtension(project.FullFwDataPath)))
            if (IsInside(destination, owned, windows) || IsInside(real, ResolveRealPath(owned, windows), windows)) return owned;
        return null;
    }

    /// <summary>
    /// The path with every link on it followed (symlinks and junctions to their final target) and, on Windows, 8.3 short
    /// names replaced by long ones. The part of the path that does not exist yet is kept as written, after the real
    /// path of its deepest existing ancestor.
    /// </summary>
    internal static string ResolveRealPath(string path, bool windows = false, int depth = 0)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        var current = root;
        var segments = full[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            var next = Path.Combine(current, segments[index]);
            FileSystemInfo info = Directory.Exists(next) ? new DirectoryInfo(next) : new FileInfo(next);
            if (info.LinkTarget is not null && depth < 32)
            {
                string? target = null;
                try { target = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName; }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
                // A dangling or looping link keeps its own name; its target is then resolved from the link's folder.
                target ??= Path.GetFullPath(info.LinkTarget, current);
                next = ResolveRealPath(target, windows, depth + 1);
            }
            else if (windows && info.Exists && new DirectoryInfo(current).EnumerateFileSystemInfos(segments[index]).FirstOrDefault()
                is { } named)
                next = Path.Combine(current, named.Name);
            current = next;
            if (!info.Exists && info.LinkTarget is null)
            {
                for (var rest = index + 1; rest < segments.Length; rest++) current = Path.Combine(current, segments[rest]);
                break;
            }
        }
        return current;
    }

    /// <summary>The store file, the folders Motif owns beside it, and the folder pre-restore copies go in.</summary>
    internal static IEnumerable<string> OwnedPaths(string storePath, string parent, string stem) =>
    [
        storePath, MotifDatabase.TrialExportDirectory(storePath), Path.Combine(parent, stem + ".motif-backups"),
    ];

    /// <summary>Whether <paramref name="path"/> is <paramref name="root"/> or lies under it, by whole path segments.</summary>
    internal static bool IsInside(string path, string root, bool windows)
    {
        string Normal(string value)
        {
            if (windows) value = value.Replace('\\', '/');
            return value.Length > 1 ? value.TrimEnd('/') : value;
        }
        var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var (p, r) = (Normal(path), Normal(root));
        return string.Equals(p, r, comparison) || p.StartsWith(r + "/", comparison);
    }

    private static StringComparer FileSystemComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool IsStagedName(string path) => StagedSuffix().IsMatch(path);

    [System.Text.RegularExpressions.GeneratedRegex(@"\.restore-[0-9a-f]{32}$")]
    private static partial System.Text.RegularExpressions.Regex StagedSuffix();

    /// <summary>Deletes staged files a crashed restore left behind.</summary>
    private static void RemoveLeftoverStagedFiles(ProjectLocator project, string storePath)
    {
        var parent = Path.GetDirectoryName(storePath)!;
        var stem = Path.GetFileNameWithoutExtension(project.FullFwDataPath);
        var leftovers = new List<string>();
        if (Directory.Exists(parent))
            leftovers.AddRange(Directory.EnumerateFiles(parent, stem + ".motif*.restore-*"));
        var trialRoot = MotifDatabase.TrialExportDirectory(storePath);
        if (Directory.Exists(trialRoot))
            leftovers.AddRange(Directory.EnumerateFiles(trialRoot, "*.restore-*", SearchOption.AllDirectories));
        foreach (var path in leftovers.Where(IsStagedName))
            try { File.Delete(path); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Whether the backup is of this project: by LibLCM project GUID, else by identity.</summary>
    private static bool SameProject(StoreBackupResponse receipt, ProjectLocator project)
    {
        var current = FwDataProjectGuid.TryRead(project.FullFwDataPath);
        if (receipt.ProjectGuid is not null && current is not null)
            return string.Equals(receipt.ProjectGuid, current, StringComparison.OrdinalIgnoreCase);
        return string.Equals(receipt.ProjectIdentity, project.FieldWorksProjectIdentity, StringComparison.Ordinal);
    }

    /// <summary>Where a restore is, for tests that fail or stop it between steps.</summary>
    [ThreadStatic]
    internal static Action<string>? StepHook;

    private static void Step(string name) => StepHook?.Invoke(name);

    private sealed class RestoreProgress
    {
        public string? Directory { get; set; }
    }

    private static StoreBackupResponse WriteBackup(ProjectLocator project, string storePath, string destination,
        DateTimeOffset now, Func<string, StoreFileFacts> copyStore)
    {
        Directory.CreateDirectory(destination);
        var files = new List<StoreBackupFile>();
        var storeCopy = Path.Combine(destination, StoreName);
        var facts = copyStore(storeCopy);
        files.Add(Describe("store", StoreName, storeCopy));

        void CopyOwned(string role, string name, string source)
        {
            if (!File.Exists(source)) return;
            var copy = Path.Combine(destination, name.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            CopyShared(source, copy);
            files.Add(Describe(role, name, copy));
        }

        CopyOwned("config", ConfigName, Path.ChangeExtension(project.FullFwDataPath, ".motif.toml"));
        CopyOwned("upgrade-report", UpgradeReportName, storePath + ".upgrade.json");
        var trialRoot = MotifDatabase.TrialExportDirectory(storePath);
        if (Directory.Exists(trialRoot))
            foreach (var source in Directory.EnumerateFiles(trialRoot, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                CopyOwned("trial-export", TrialExportsPrefix + Path.GetRelativePath(trialRoot, source).Replace('\\', '/'), source);

        var receiptPath = Path.Combine(destination, ReceiptName);
        var receipt = new StoreBackupResponse(destination, receiptPath, project.FullFwDataPath,
            FwDataProjectGuid.TryRead(project.FullFwDataPath), project.FieldWorksProjectIdentity, facts.SchemaVersion,
            MotifProductVersion.CurrentText, now, files, false, FwDataNote);
        // Written beside, then renamed in, so a half-written receipt is never mistaken for a finished backup.
        var receiptTemp = receiptPath + ".tmp";
        File.WriteAllText(receiptTemp, JsonSerializer.Serialize(receipt, ReceiptJson));
        File.Move(receiptTemp, receiptPath, overwrite: true);
        return receipt;
    }

    private sealed record Read((string Directory, StoreBackupResponse Receipt, List<Target> Targets) Value, Refusal? Refusal);

    private static Read ReadReceipt(string backupPath, ProjectLocator project, string storePath)
    {
        Read Invalid(string why) => new(default, new Refusal(RefusalCodes.StoreRestoreInvalid, FailureReason.InvalidArgument,
            why + " Name a folder made by 'motif store backup'.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["backupPath"] = backupPath }));

        var full = Path.GetFullPath(backupPath);
        var directory = File.Exists(full) && Path.GetFileName(full) == ReceiptName ? Path.GetDirectoryName(full)! : full;
        var receiptPath = Path.Combine(directory, ReceiptName);
        if (!File.Exists(receiptPath)) return Invalid($"'{directory}' has no {ReceiptName}.");
        StoreBackupResponse? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<StoreBackupResponse>(File.ReadAllText(receiptPath), ReceiptJson);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Invalid($"'{receiptPath}' cannot be read: {exception.Message}");
        }
        if (receipt?.Files is null || string.IsNullOrWhiteSpace(receipt.ProjectPath) ||
            string.IsNullOrWhiteSpace(receipt.ProjectIdentity) || receipt.SchemaVersion < 1)
            return Invalid($"'{receiptPath}' is not a Motif backup receipt.");

        var targets = new List<Target>();
        foreach (var entry in receipt.Files)
        {
            if (!IsPlainRelativeName(entry.Name) || entry.Sha256 is null)
                return Invalid($"The receipt names a file it cannot restore: '{entry.Name}'.");
            var destination = entry.Role switch
            {
                "store" when entry.Name == StoreName => storePath,
                "config" when entry.Name == ConfigName => Path.ChangeExtension(project.FullFwDataPath, ".motif.toml"),
                "upgrade-report" when entry.Name == UpgradeReportName => storePath + ".upgrade.json",
                "trial-export" when entry.Name.StartsWith(TrialExportsPrefix, StringComparison.Ordinal) =>
                    Path.Combine(MotifDatabase.TrialExportDirectory(storePath),
                        entry.Name[TrialExportsPrefix.Length..].Replace('/', Path.DirectorySeparatorChar)),
                _ => null,
            };
            if (destination is null) return Invalid($"The receipt lists '{entry.Name}' with the unknown role '{entry.Role}'.");
            if (!File.Exists(Path.Combine(directory, entry.Name.Replace('/', Path.DirectorySeparatorChar))))
                return Invalid($"The receipt lists '{entry.Name}', which is not in the backup folder.");
            if (targets.Any(target => FileSystemComparer.Equals(target.Destination, destination)))
                return Invalid($"The receipt lists '{entry.Name}' more than once, or two names for one file.");
            targets.Add(new Target(entry, destination));
        }
        if (targets.Count(target => target.Entry.Role == "store") != 1)
            return Invalid("The receipt must list exactly one store.");
        return new Read((directory, receipt, targets), null);
    }

    /// <summary>
    /// A receipt file name that is the same on every OS: no empty, rooted, drive-qualified, UNC or <c>..</c> segment,
    /// and none of Windows' reserved characters, so a receipt made on one OS cannot reach outside the backup on another.
    /// </summary>
    internal static bool IsPlainRelativeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(['\0', ':', '*', '?', '"', '<', '>', '|']) >= 0) return false;
        var segments = name.Split('/', '\\');
        return segments.All(segment => segment.Length > 0 && segment is not ("." or "..") && segment.TrimEnd() == segment
            && !segment.EndsWith('.'));
    }

    private sealed record Target(StoreBackupFile Entry, string Destination);

    private sealed class StagedFile(Target target, string stagedPath)
    {
        public Target Target { get; } = target;
        public string StagedPath { get; } = stagedPath;

        public void Discard()
        {
            try { if (File.Exists(StagedPath)) File.Delete(StagedPath); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }

        public void SwapIn()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Target.Destination)!);
            File.Move(StagedPath, Target.Destination, overwrite: true);
        }
    }

    private static CommandOutcome<T> NewerSchema<T>(int schema, ProjectLocator project, string storePath) where T : class =>
        Refuse<T>(RefusalCodes.StoreRestoreNewerSchema, FailureReason.Refused,
            $"This backup's store is at schema {schema} and this build of Motif understands {MotifSchema.CurrentSchema}. " +
            "Update Motif and restore it again; nothing was changed.", project, storePath,
            ("backupSchema", schema.ToString(CultureInfo.InvariantCulture)),
            ("supportedSchema", MotifSchema.CurrentSchema.ToString(CultureInfo.InvariantCulture)));

    private static CommandOutcome<StoreRestoreResponse> NewerSchema(int schema, ProjectLocator project, string storePath) =>
        NewerSchema<StoreRestoreResponse>(schema, project, storePath);

    private static CommandOutcome<T> Failed<T>(Exception exception, ProjectLocator project, string storePath, string action,
        string? savedIn = null) where T : class
    {
        if (exception is MotifStoreLockException)
        {
            var busy = Refuse<T>(RefusalCodes.ProjectBusy, FailureReason.Busy,
                $"The project store is in use by another Motif process, so it cannot be {(action == "restore" ? "restored" : "backed up")} now. " +
                "Close it and try again.", project, storePath, ("exceptionDetail", exception.Message));
            return CommandOutcome<T>.Refused(ProjectStoreCommand.NamingHolders(busy.Refusal!, storePath));
        }
        // A store another process kept locked past the busy timeout is the CLI's retryable cli.store-busy.
        if (SqliteConnections.IsBusyInChain(exception)) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(exception);
        // Windows reports a file another process holds open as a sharing violation; name that cause.
        if (IsSharingViolation(exception))
            return Refuse<T>(RefusalCodes.ProjectStoreInUse, FailureReason.Busy,
                "Another process has the store open; close Motif sessions and retry. " +
                $"Nothing was changed{(savedIn is null ? "" : $", and the store and any file moved aside are as they were (see '{savedIn}')")}.",
                project, storePath, ("exceptionDetail", exception.Message), ("preRestoreDirectory", savedIn ?? ""));
        return Refuse<T>(RefusalCodes.ProjectStoreIo, FailureReason.Refused,
            $"Motif could not {action} its file for this project, '{Path.GetFileName(storePath)}'." +
            (savedIn is null ? "" : $" The store it was replacing, and any file moved aside, are in '{savedIn}'."),
            project, storePath, ("exceptionDetail", exception.Message), ("preRestoreDirectory", savedIn ?? ""));
    }

    /// <summary>True for a Windows sharing or lock violation in the exception chain.</summary>
    private static bool IsSharingViolation(Exception? exception)
    {
        if (!OperatingSystem.IsWindows()) return false;
        for (; exception is not null; exception = exception.InnerException)
            if (exception is IOException && (exception.HResult & 0xFFFF) is 32 or 33) return true;
        return false;
    }

    private static CommandOutcome<T> Refuse<T>(string code, FailureReason reason, string message, ProjectLocator project,
        string storePath, params (string Name, string Value)[] facts) where T : class
    {
        var all = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["fwDataPath"] = project.FullFwDataPath,
            [RefusalFactNames.StorePath] = storePath,
        };
        foreach (var (name, value) in facts) all[name] = value;
        return CommandOutcome<T>.Refused(new Refusal(code, reason, message, all));
    }

    private static StoreBackupFile Describe(string role, string name, string path)
    {
        var (bytes, sha) = Measure(path);
        return new StoreBackupFile(role, name, path, bytes, sha);
    }

    private static (long Bytes, string Sha256) Measure(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return (stream.Length, Convert.ToHexStringLower(hash));
    }

    private static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    private static void Discard(string destination, bool created)
    {
        try
        {
            if (created) Directory.Delete(destination, recursive: true);
            else foreach (var entry in Directory.EnumerateFileSystemEntries(destination)) File.Delete(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string Stamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
}
