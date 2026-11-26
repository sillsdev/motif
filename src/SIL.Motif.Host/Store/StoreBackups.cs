using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Host.Store;

/// <summary>
/// A backup Motif took on its own before it changed a store: the folder it is in and its receipt.
/// </summary>
/// <param name="Directory">The backup folder, which <c>motif store restore</c> takes.</param>
/// <param name="ReceiptPath">The folder's <c>motif-backup.json</c>.</param>
public sealed record StoreSafetyBackup(string Directory, string ReceiptPath);

/// <summary>What a reset did: whether it deleted the store, and the backup it took first.</summary>
/// <param name="Deleted">Whether the store file was deleted.</param>
/// <param name="Backup">The backup taken before the deletion; null when nothing was deleted.</param>
public sealed record StoreDeletion(bool Deleted, StoreSafetyBackup? Backup);

/// <summary>
/// Writes and checks the backup folder that <c>motif store backup</c>, a store upgrade and a store reset all share,
/// so one receipt format and one set of files serve <c>motif store restore</c> whichever of them wrote it.
/// </summary>
/// <remarks>
/// A backup is the store (copied with SQLite's online backup), the project's <c>.motif.toml</c>, the store's upgrade
/// report and its Trial evidence folder when they exist, and <c>motif-backup.json</c> with each file's size and sha256.
/// </remarks>
public static class StoreBackups
{
    /// <summary>The reason a backup taken before a store upgrade is named for.</summary>
    public const string PreUpgrade = "pre-upgrade";

    /// <summary>The reason a backup taken before a store reset is named for.</summary>
    public const string PreReset = "pre-reset";

    /// <summary>The receipt's file name inside a backup folder.</summary>
    public const string ReceiptName = "motif-backup.json";

    /// <summary>The store's name inside a backup folder.</summary>
    public const string StoreName = "store.sqlite";

    /// <summary>The project configuration's name inside a backup folder.</summary>
    public const string ConfigName = "Config.motif.toml";

    /// <summary>The store's upgrade report's name inside a backup folder.</summary>
    public const string UpgradeReportName = "store.upgrade.json";

    /// <summary>The prefix of a Trial evidence file's name inside a backup folder.</summary>
    public const string TrialExportsPrefix = "trial-exports/";

    private const string FwDataNote =
        "The FieldWorks project file (.fwdata) is not part of this backup. Back it up with FieldWorks' own store backup.";

    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The JSON options a receipt is written and read with.</summary>
    public static JsonSerializerOptions ReceiptOptions => ReceiptJson;

    /// <summary>The folder that holds a project's pre-upgrade, pre-reset and pre-restore backups.</summary>
    public static string BackupsRoot(string storePath, ProjectLocator project) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(storePath))!,
            Path.GetFileNameWithoutExtension(project.FullFwDataPath) + ".motif-backups");

    /// <summary>The timestamp every backup folder name carries.</summary>
    public static string Stamp(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Writes one backup folder: <paramref name="copyStore"/> makes the store copy, then each owned file beside the
    /// store is copied, and the receipt is renamed in last, so a half-written backup never has a receipt.
    /// </summary>
    public static StoreBackupResponse Write(ProjectLocator project, string storePath, string destination,
        DateTimeOffset now, Func<string, StoreFileFacts> copyStore)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(copyStore);
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

    /// <summary>
    /// Reads every file of a backup back and checks it against its receipt, and that its store is a Motif store at
    /// <paramref name="schema"/>, so a backup is trusted before the store it protects is changed.
    /// </summary>
    /// <exception cref="InvalidDataException">A file differs from its receipt, or the stored copy is not the expected store.</exception>
    public static void Verify(StoreBackupResponse receipt, int schema)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (!File.Exists(receipt.ReceiptPath)) throw new InvalidDataException("The backup has no receipt.");
        foreach (var file in receipt.Files)
        {
            if (!File.Exists(file.Path)) throw new InvalidDataException($"The backup lacks '{file.Name}'.");
            var (bytes, sha) = Measure(file.Path);
            if (bytes != file.Bytes || !string.Equals(sha, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"'{file.Name}' in the backup does not match its receipt.");
        }
        var store = receipt.Files.Single(file => file.Role == "store");
        if (MotifDatabase.ReadStoreFileFacts(store.Path).SchemaVersion != schema)
            throw new InvalidDataException("The backup's store is not at the schema that was backed up.");
    }

    /// <summary>
    /// Backs the store up for a caller that already holds it exclusively (creation lock and exclusive lease), then
    /// verifies the backup. The folder is <c>&lt;stem&gt;.motif-backups/&lt;reason&gt;-schema&lt;N&gt;-&lt;time&gt;</c>.
    /// A failed backup leaves no folder behind.
    /// </summary>
    /// <param name="project">The project the store belongs to.</param>
    /// <param name="storePath">The store being protected.</param>
    /// <param name="reason">Names the folder: <c>pre-upgrade</c> or <c>pre-reset</c>.</param>
    /// <param name="clock">The clock for the folder's name and the receipt, or <see langword="null"/> for system time.</param>
    /// <exception cref="IOException">The store could not be copied, or the folder could not be written.</exception>
    /// <exception cref="InvalidDataException">The store is not readable, or the written backup failed verification.</exception>
    public static StoreSafetyBackup WriteHeldExclusively(ProjectLocator project, string storePath, string reason,
        TimeProvider? clock = null)
    {
        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var root = BackupsRoot(storePath, project);
        var schema = MotifDatabase.ReadStoreFileFacts(storePath).SchemaVersion;
        var name = $"{reason}-schema{schema.ToString(CultureInfo.InvariantCulture)}-{Stamp(now)}";
        var destination = Path.Combine(root, name);
        for (var n = 2; Directory.Exists(destination) || File.Exists(destination); n++)
            destination = Path.Combine(root, name + "-" + n.ToString(CultureInfo.InvariantCulture));
        try
        {
            MotifDatabase.CheckpointHeldExclusively(storePath);
            var receipt = Write(project, storePath, destination, now,
                file => MotifDatabase.BackUpHeldExclusively(storePath, file));
            Verify(receipt, schema);
            return new StoreSafetyBackup(destination, receipt.ReceiptPath);
        }
        catch
        {
            Discard(destination);
            throw;
        }
    }

    /// <summary>
    /// Whether the backup behind <paramref name="receiptPath"/> is good enough to let the exact original go: its files match
    /// the receipt's sha256, its store opens read-only, passes <c>integrity_check</c>, is at <paramref name="schema"/>, and
    /// holds the same row count in every table as <paramref name="original"/>. Never throws.
    /// </summary>
    public static bool MatchesOriginal(string receiptPath, string original, int schema, out string why)
    {
        why = "";
        try
        {
            var receipt = JsonSerializer.Deserialize<StoreBackupResponse>(File.ReadAllText(receiptPath), ReceiptJson)
                ?? throw new InvalidDataException("The receipt is empty.");
            Verify(receipt, schema);
            var store = receipt.Files.Single(file => file.Role == "store").Path;
            var backupCounts = Check(store, schema, integrity: true);
            var originalCounts = Check(original, schema, integrity: false);
            foreach (var pair in originalCounts)
                if (!backupCounts.TryGetValue(pair.Key, out var count) || count != pair.Value)
                    throw new InvalidDataException($"Table {pair.Key} holds {pair.Value} rows in the original and a different count in the backup.");
            if (backupCounts.Count != originalCounts.Count) throw new InvalidDataException("The tables differ.");
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or
            InvalidOperationException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        {
            why = exception.Message;
            return false;
        }
    }

    private static Dictionary<string, long> Check(string path, int schema, bool integrity)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = path, Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        object? Scalar(string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return command.ExecuteScalar();
        }
        if (integrity && !string.Equals(Scalar("PRAGMA integrity_check;") as string, "ok", StringComparison.Ordinal))
            throw new InvalidDataException("The backup's integrity check failed.");
        if (Convert.ToInt32(Scalar("PRAGMA user_version;"), CultureInfo.InvariantCulture) != schema)
            throw new InvalidDataException("A store is not at the schema that was backed up.");
        var tables = new List<string>();
        using (var list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';";
            using var reader = list.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var table in tables)
            counts[table] = Convert.ToInt64(Scalar("SELECT COUNT(*) FROM \"" + table.Replace("\"", "\"\"") + "\";"), CultureInfo.InvariantCulture);
        return counts;
    }

    /// <summary>Removes a backup folder that was never finished.</summary>
    public static void Discard(string destination)
    {
        try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static StoreBackupFile Describe(string role, string name, string path)
    {
        var (bytes, sha) = Measure(path);
        return new StoreBackupFile(role, name, path, bytes, sha);
    }

    /// <summary>A file's size and lowercase hexadecimal sha256.</summary>
    public static (long Bytes, string Sha256) Measure(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var hash = SHA256.HashData(stream);
        return (stream.Length, Convert.ToHexStringLower(hash));
    }

    /// <summary>Copies a file that another process may have open.</summary>
    public static void CopyShared(string source, string destination)
    {
        using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }
}
