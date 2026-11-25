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
/// A restore checks the receipt, the schema and the project, then, under the store's exclusive lease, stages
/// every file beside its target, checks the staged copy's sha256, backs up the store it replaces and swaps the
/// staged files in by rename. Every refusal leaves the store as it was, pinned by
/// `AFileThatDoesNotMatchItsShaIsRefusedAndTheStoreIsLeftAsItWas`.
/// </para>
/// </remarks>
public static class ProjectBackupCommand
{
    /// <summary>The receipt's file name inside a backup folder.</summary>
    public const string ReceiptName = "motif-backup.json";

    private const string StoreName = "store.sqlite";
    private const string ConfigName = "Config.motif.toml";
    private const string UpgradeReportName = "store.upgrade.json";
    private const string TrialExportsPrefix = "trial-exports/";
    private const string FwDataNote =
        "The FieldWorks project file (.fwdata) is not part of this backup. Back it up with FieldWorks' own project backup.";

    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>Writes a consistent copy of the project's store and owned files into a new folder.</summary>
    public static CommandOutcome<ProjectBackupResponse> Backup(ProjectBackupRequest request) => Backup(request, null);

    internal static CommandOutcome<ProjectBackupResponse> Backup(ProjectBackupRequest request, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(request);
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<ProjectBackupResponse>.Refused(located.Refusal!);
        var project = located.Value!;
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        if (!File.Exists(storePath))
            return Refuse<ProjectBackupResponse>(RefusalCodes.ProjectBackupNoStore, FailureReason.NotFound,
                $"There is no Motif store, '{Path.GetFileName(storePath)}', beside this project to back up.", project, storePath);

        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var destination = string.IsNullOrWhiteSpace(request.To)
            ? Path.Combine(Path.GetDirectoryName(storePath)!,
                Path.GetFileNameWithoutExtension(project.FullFwDataPath) + ".motif-backup-" + Stamp(now))
            : Path.GetFullPath(request.To);
        if (File.Exists(destination) || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
            return Refuse<ProjectBackupResponse>(RefusalCodes.ProjectBackupDestinationExists, FailureReason.InvalidArgument,
                $"'{destination}' already exists and is not empty. Name a new folder with --to, or leave it off for a timestamped one.",
                project, storePath, ("destination", destination));

        var created = !Directory.Exists(destination);
        try
        {
            return CommandOutcome<ProjectBackupResponse>.Success(
                WriteBackup(project, storePath, destination, now, file => MotifDatabase.BackUpExisting(storePath, file)));
        }
        catch (Exception exception) when (exception is MotifStoreLockException or InvalidDataException or IOException
            or UnauthorizedAccessException)
        {
            Discard(destination, created);
            return Failed<ProjectBackupResponse>(exception, project, storePath, "back up");
        }
    }

    /// <summary>Puts a backup's store and owned files back, after saving the store they replace.</summary>
    public static CommandOutcome<ProjectRestoreResponse> Restore(ProjectRestoreRequest request) => Restore(request, null);

    internal static CommandOutcome<ProjectRestoreResponse> Restore(ProjectRestoreRequest request, TimeProvider? clock)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.BackupPath))
            return CommandOutcome<ProjectRestoreResponse>.Refused(new Refusal(RefusalCodes.ProjectBackupInvalid,
                FailureReason.InvalidArgument, "A backup folder is required.", new Dictionary<string, string>()));
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<ProjectRestoreResponse>.Refused(located.Refusal!);
        var project = located.Value!;
        var storePath = ProjectDatabaseCatalog.DatabasePathFor(project);

        var read = ReadReceipt(request.BackupPath, project, storePath);
        if (read.Refusal is { } refusal) return CommandOutcome<ProjectRestoreResponse>.Refused(refusal);
        var (directory, receipt, targets) = read.Value;

        if (receipt.SchemaVersion > MotifSchema.CurrentSchema)
            return NewerSchema(receipt.SchemaVersion, project, storePath);
        if (!string.Equals(receipt.ProjectPath, project.FullFwDataPath, StringComparison.OrdinalIgnoreCase))
            return Refuse<ProjectRestoreResponse>(RefusalCodes.ProjectBackupOtherProject, FailureReason.Refused,
                $"This backup is of the store for '{receipt.ProjectPath}', not for '{project.FullFwDataPath}'. " +
                "Restore it for that project; nothing was changed.", project, storePath,
                (RefusalFactNames.RegisteredProjectPath, receipt.ProjectPath));

        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var staging = Guid.NewGuid().ToString("N");
        var staged = new List<StagedFile>();
        try
        {
            return MotifDatabase.WithExclusiveStoreFiles(storePath, null, () =>
                RestoreLocked(project, storePath, directory, receipt, targets, staging, staged, now));
        }
        catch (Exception exception) when (exception is MotifStoreLockException or InvalidDataException or IOException
            or UnauthorizedAccessException)
        {
            foreach (var file in staged) file.Discard();
            return Failed<ProjectRestoreResponse>(exception, project, storePath, "restore");
        }
    }

    private static CommandOutcome<ProjectRestoreResponse> RestoreLocked(ProjectLocator project, string storePath,
        string directory, ProjectBackupResponse receipt, List<Target> targets, string staging, List<StagedFile> staged,
        DateTimeOffset now)
    {
        foreach (var target in targets)
        {
            var stagedPath = target.Destination + ".restore-" + staging;
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
            var file = new StagedFile(target, stagedPath);
            staged.Add(file);
            CopyShared(Path.Combine(directory, target.Entry.Name), stagedPath);
            var (bytes, sha) = Measure(stagedPath);
            if (bytes != target.Entry.Bytes || !string.Equals(sha, target.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var each in staged) each.Discard();
                return Refuse<ProjectRestoreResponse>(RefusalCodes.ProjectBackupChecksumMismatch, FailureReason.Refused,
                    $"'{target.Entry.Name}' in the backup does not match its receipt (expected sha256 " +
                    $"{target.Entry.Sha256}, found {sha}). The backup is damaged or was edited; nothing was changed.",
                    project, storePath, ("file", target.Entry.Name), ("expectedSha256", target.Entry.Sha256), ("actualSha256", sha));
            }
        }

        var stagedStore = staged.First(file => file.Target.Entry.Role == "store").StagedPath;
        StoreFileFacts facts;
        try
        {
            facts = MotifDatabase.ReadStoreFileFacts(stagedStore);
        }
        catch (InvalidDataException exception)
        {
            foreach (var each in staged) each.Discard();
            return Refuse<ProjectRestoreResponse>(RefusalCodes.ProjectBackupInvalid, FailureReason.InvalidArgument,
                "The backup's store is not a readable Motif store: " + exception.Message, project, storePath);
        }
        if (facts.SchemaVersion > MotifSchema.CurrentSchema)
        {
            foreach (var each in staged) each.Discard();
            return NewerSchema(facts.SchemaVersion, project, storePath);
        }

        ProjectBackupResponse? previous = null;
        if (File.Exists(storePath))
        {
            var saved = Path.Combine(Path.GetDirectoryName(storePath)!,
                Path.GetFileNameWithoutExtension(project.FullFwDataPath) + ".motif-backups", "pre-restore-" + Stamp(now));
            previous = WriteBackup(project, storePath, saved, now, file => MotifDatabase.BackUpHeldExclusively(storePath, file));
        }

        foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(storePath + suffix)) File.Delete(storePath + suffix);
        foreach (var file in staged) file.SwapIn();

        var restored = targets.Select(target => new ProjectBackupFile(target.Entry.Role, target.Entry.Name,
            target.Destination, target.Entry.Bytes, target.Entry.Sha256)).ToList();
        return CommandOutcome<ProjectRestoreResponse>.Success(new ProjectRestoreResponse(
            storePath, directory, facts.SchemaVersion, now, restored, previous));
    }

    private static ProjectBackupResponse WriteBackup(ProjectLocator project, string storePath, string destination,
        DateTimeOffset now, Func<string, StoreFileFacts> copyStore)
    {
        Directory.CreateDirectory(destination);
        var files = new List<ProjectBackupFile>();
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
        var receipt = new ProjectBackupResponse(destination, receiptPath, project.FullFwDataPath, facts.SchemaVersion,
            MotifProductVersion.CurrentText, now, files, false, FwDataNote);
        File.WriteAllText(receiptPath, JsonSerializer.Serialize(receipt, ReceiptJson));
        return receipt;
    }

    private sealed record Read((string Directory, ProjectBackupResponse Receipt, List<Target> Targets) Value, Refusal? Refusal);

    private static Read ReadReceipt(string backupPath, ProjectLocator project, string storePath)
    {
        Read Invalid(string why) => new(default, new Refusal(RefusalCodes.ProjectBackupInvalid, FailureReason.InvalidArgument,
            why + " Name a folder made by 'motif project backup'.",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["backupPath"] = backupPath }));

        var full = Path.GetFullPath(backupPath);
        var directory = File.Exists(full) && Path.GetFileName(full) == ReceiptName ? Path.GetDirectoryName(full)! : full;
        var receiptPath = Path.Combine(directory, ReceiptName);
        if (!File.Exists(receiptPath)) return Invalid($"'{directory}' has no {ReceiptName}.");
        ProjectBackupResponse? receipt;
        try
        {
            receipt = JsonSerializer.Deserialize<ProjectBackupResponse>(File.ReadAllText(receiptPath), ReceiptJson);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return Invalid($"'{receiptPath}' cannot be read: {exception.Message}");
        }
        if (receipt?.Files is null || string.IsNullOrWhiteSpace(receipt.ProjectPath) || receipt.SchemaVersion < 1)
            return Invalid($"'{receiptPath}' is not a Motif backup receipt.");

        var targets = new List<Target>();
        foreach (var entry in receipt.Files)
        {
            if (string.IsNullOrWhiteSpace(entry.Name) || entry.Sha256 is null || Path.IsPathRooted(entry.Name) ||
                entry.Name.Split('/', '\\').Contains(".."))
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
            targets.Add(new Target(entry, destination));
        }
        if (targets.Count(target => target.Entry.Role == "store") != 1)
            return Invalid("The receipt must list exactly one store.");
        return new Read((directory, receipt, targets), null);
    }

    private sealed record Target(ProjectBackupFile Entry, string Destination);

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
        Refuse<T>(RefusalCodes.ProjectBackupNewerSchema, FailureReason.Refused,
            $"This backup's store is at schema {schema} and this build of Motif understands {MotifSchema.CurrentSchema}. " +
            "Update Motif and restore it again; nothing was changed.", project, storePath,
            ("backupSchema", schema.ToString(CultureInfo.InvariantCulture)),
            ("supportedSchema", MotifSchema.CurrentSchema.ToString(CultureInfo.InvariantCulture)));

    private static CommandOutcome<ProjectRestoreResponse> NewerSchema(int schema, ProjectLocator project, string storePath) =>
        NewerSchema<ProjectRestoreResponse>(schema, project, storePath);

    private static CommandOutcome<T> Failed<T>(Exception exception, ProjectLocator project, string storePath, string action)
        where T : class
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
        return Refuse<T>(RefusalCodes.ProjectStoreIo, FailureReason.Refused,
            $"Motif could not {action} its file for this project, '{Path.GetFileName(storePath)}'.",
            project, storePath, ("exceptionDetail", exception.Message));
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

    private static ProjectBackupFile Describe(string role, string name, string path)
    {
        var (bytes, sha) = Measure(path);
        return new ProjectBackupFile(role, name, path, bytes, sha);
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
