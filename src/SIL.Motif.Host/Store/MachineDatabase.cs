using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>Represents one process's connection boundary onto the machine store.</summary>
/// <remarks>
/// One machine store exists per logged-in user, in the worker root, holding <c>KnownProjects</c> and
/// <c>Usage</c> — nothing that belongs to any one project. A thin wrapper over
/// <see cref="MotifSqliteStore"/>, sharing its open-and-create ceremony and connection lifecycle with
/// <see cref="MotifDatabase"/>; this type owns only the <see cref="MachineSchema"/> descriptor and its own
/// path resolution.
/// </remarks>
public sealed class MachineDatabase : IDisposable
{
    private const string DatabaseName = "motif.db";

    private readonly MotifSqliteStore _store;

    private MachineDatabase(MotifSqliteStore store) => _store = store;

    /// <summary>Opens and owns the machine store at <c>&lt;root&gt;/motif.db</c>, creating it if absent.</summary>
    /// <param name="root">The worker root this installation uses for the logged-in user.</param>
    /// <returns>An owned database boundary whose connections are configured for worker use.</returns>
    /// <exception cref="InvalidDataException">The file identity or schema generation is invalid.</exception>
    /// <exception cref="NotSupportedException">The schema generation is newer than this build understands.</exception>
    public static MachineDatabase Open(string root) => Open(root, null);

    /// <summary>Opens the machine store with a clock that names a rebuild's backup folder, for tests.</summary>
    internal static MachineDatabase Open(string root, TimeProvider? clock)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A worker root is required.", nameof(root));

        var path = Path.Combine(root, "motif.db");
        var descriptor = new MotifSqliteStoreDescriptor
        {
            Name = "Motif machine database",
            ApplicationId = MachineSchema.ApplicationId,
            CurrentSchema = MachineSchema.CurrentSchema,
            ValidateSchema = MachineSchema.ValidateSchema,
            Create = MachineSchema.Create,
            // Only recomputable records live here, so an older store is rebuilt empty after a backup (ADR 0023).
            RebuildOlder = true,
            NoticeName = "machine store",
            BackUp = _ => BackUpBeforeRebuild(path, clock),
            Retain = safety => StoreBackupRetention.Prune(
                Path.GetDirectoryName(safety.Directory)!, StoreBackups.PreUpgrade, safety.Directory, MachineSchema.CurrentSchema)
        };
        return new MachineDatabase(MotifSqliteStore.Open(path, descriptor));
    }

    /// <summary>The folder that holds the machine store's pre-upgrade backups, beside <c>motif.db</c>.</summary>
    public static string BackupsRoot(string root) => Path.Combine(Path.GetFullPath(root), DatabaseName + "-backups");

    // Copies the held store into motif.db-backups/pre-upgrade-schemaN-time, receipt last, and reads the copy back.
    private static StoreSafetyBackup BackUpBeforeRebuild(string path, TimeProvider? clock)
    {
        path = Path.GetFullPath(path);
        var now = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow;
        var schema = ReadSchema(path);
        var name = $"{StoreBackups.PreUpgrade}-schema{schema.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{StoreBackups.Stamp(now)}";
        var backups = BackupsRoot(Path.GetDirectoryName(path)!);
        var destination = Path.Combine(backups, name);
        for (var n = 2; Directory.Exists(destination); n++)
            destination = Path.Combine(backups, name + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            Directory.CreateDirectory(destination);
            var copy = Path.Combine(destination, StoreBackups.StoreName);
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 30 }.ToString()))
            {
                source.Open();
                using (var checkpoint = source.CreateCommand())
                {
                    checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                    checkpoint.ExecuteNonQuery();
                }
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = copy, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
                target.Open();
                source.BackupDatabase(target);
                using var journal = target.CreateCommand();
                journal.CommandText = "PRAGMA journal_mode = DELETE;";
                journal.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            if (ReadSchema(copy) != schema) throw new InvalidDataException("The backup's store is not at the schema that was backed up.");
            var (bytes, sha) = StoreBackups.Measure(copy);
            var receiptPath = Path.Combine(destination, StoreBackups.ReceiptName);
            var receipt = new
            {
                kind = "machine-store",
                schemaVersion = schema,
                productVersion = MotifProductVersion.CurrentText,
                createdUtc = now,
                files = new[] { new { role = "store", name = StoreBackups.StoreName, bytes, sha256 = sha } }
            };
            File.WriteAllText(receiptPath + ".tmp", System.Text.Json.JsonSerializer.Serialize(receipt, StoreBackups.ReceiptOptions));
            File.Move(receiptPath + ".tmp", receiptPath, overwrite: true);
            return new StoreSafetyBackup(destination, receiptPath);
        }
        catch
        {
            StoreBackups.Discard(destination);
            throw;
        }
    }

    private static int ReadSchema(string path)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Opens a configured connection while this process owns the database.</summary>
    public SqliteConnection OpenConnection() => _store.OpenConnection();

    /// <summary>Gets the fully resolved path of the owned machine database.</summary>
    public string FullPath => _store.FullPath;

    internal int TrackedConnectionCount => _store.TrackedConnectionCount;

    /// <summary>Closes this process's connections. Nothing is released for anyone else.</summary>
    public void Dispose() => _store.Dispose();
}
