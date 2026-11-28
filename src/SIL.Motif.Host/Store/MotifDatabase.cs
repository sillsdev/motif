using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;

namespace SIL.Motif.Host.Store;

/// <summary>Represents one process's connection boundary onto a project's Motif database.</summary>
/// <remarks>
/// A thin wrapper over <see cref="MotifSqliteStore"/>: this type owns only what is specific to a project
/// database — validating <see cref="ProjectLocator"/> and worker-version arguments, and the
/// <see cref="MotifSchema"/> descriptor those arguments feed. The open-and-create ceremony, shared active-use
/// lease, connection lifecycle, and failure translation it delegates to are shared with
/// <see cref="MachineDatabase"/>.
/// </remarks>
public sealed partial class MotifDatabase : IDisposable
{
    private readonly MotifSqliteStore _store;

    private MotifDatabase(MotifSqliteStore store, ProjectLocator project)
    {
        _store = store;
        Project = project;
    }

    /// <summary>The FieldWorks project paired with this database.</summary>
    public ProjectLocator Project { get; }

    /// <summary>
    /// Opens a project database with a shared active-use lease, creating it if absent. Older project stores
    /// carry authored data into the current schema; newer stores are refused.
    /// </summary>
    /// <param name="path">The sibling Motif database path.</param>
    /// <param name="project">The project locator that must match persisted metadata.</param>
    /// <param name="supportedSchema">The schema generation this worker requires; usually <see cref="MotifSchema.CurrentSchema"/>.</param>
    /// <param name="workerVersion">The worker version used for compatibility checks.</param>
    /// <param name="ownershipPatience">Maximum wait for store locks; defaults to 30 seconds.</param>
    /// <param name="timeProvider">The clock used when creating the database, or <see langword="null"/> for system time.</param>
    /// <returns>An owned database boundary whose connections are configured for worker use.</returns>
    /// <exception cref="InvalidDataException">The file identity, metadata, or project binding is invalid.</exception>
    /// <exception cref="NotSupportedException">The schema or worker compatibility is unsupported.</exception>
    public static MotifDatabase OpenOwned(
        string path,
        ProjectLocator project,
        int supportedSchema,
        Version workerVersion,
        TimeSpan? ownershipPatience = null,
        TimeProvider? timeProvider = null)
    {
        var descriptor = Describe(path, project, supportedSchema, workerVersion, timeProvider);
        return new MotifDatabase(MotifSqliteStore.Open(path, descriptor, ownershipPatience), project);
    }

    /// <summary>Opens a validated existing project store for read-only access without creating or upgrading it.</summary>
    /// <param name="path">The existing sibling Motif database path.</param>
    /// <param name="project">The project locator that must match persisted metadata.</param>
    /// <returns>A database boundary whose connections cannot write to the store.</returns>
    /// <exception cref="FileNotFoundException">The store's active-use lease does not exist.</exception>
    /// <exception cref="InvalidDataException">The file identity, metadata, or project binding is invalid.</exception>
    /// <exception cref="NotSupportedException">The store schema or worker compatibility is unsupported.</exception>
    /// <exception cref="IOException">The existing store is unavailable.</exception>
    public static MotifDatabase OpenExistingReadOnly(string path, ProjectLocator project)
    {
        var descriptor = Describe(path, project, MotifSchema.CurrentSchema, MotifProductVersion.Current);
        return new MotifDatabase(MotifSqliteStore.OpenExistingReadOnly(path, descriptor), project);
    }

    /// <summary>
    /// Deletes a project database that <see cref="OpenOwned"/> would refuse with a
    /// <see cref="MotifStoreVersionException"/>, because another version of Motif made it. Any other refusal is
    /// thrown as <see cref="OpenOwned"/> throws it, and nothing is deleted. An older store is kept for upgrade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guarantee is that this never deletes a database this version of Motif can use or upgrade. Creation
    /// and upgrade hold the creation lock this method decides and deletes under. A database created
    /// while this waits for that lock is found usable and kept, pinned by
    /// `ADatabaseCreatedWhileTheDeleteWaitsIsKept`. A newer store is never deleted: it throws
    /// <see cref="MotifStoreVersionException"/> with <see cref="MotifStoreVersionException.Newer"/> set, pinned by
    /// `ANewerStoreIsNeverResetAndIsLeftByteForByte`.
    /// </para>
    /// <para>
    /// Each open holds a shared lease on the sibling <c>.use.lock</c> file for this store's lifetime. Other Motif
    /// processes may hold the same lease, but deletion needs an exclusive lease and refuses while any opener is
    /// active, pinned by `AStoreSomethingElseHoldsOpenIsRefusedAndKept`. The lease file stays at a stable path on
    /// Unix so all processes lock the same file identity.
    /// </para>
    /// </remarks>
    /// <param name="path">The sibling Motif database path.</param>
    /// <param name="project">The project locator that must match persisted metadata.</param>
    /// <param name="supportedSchema">The schema generation this worker requires; usually <see cref="MotifSchema.CurrentSchema"/>.</param>
    /// <param name="workerVersion">The worker version used for compatibility checks.</param>
    /// <param name="ownershipPatience">Maximum wait for the creation lock; defaults to 30 seconds.</param>
    /// <param name="onWaitingForOwnership">
    /// Called once, when another opener holds the creation lock and this call starts waiting for it, so a caller
    /// can order other work against the wait. Not called when the lock is free.
    /// </param>
    /// <returns>Whether the file was deleted (not when it is absent, not yet created, or usable), and the backup taken first.</returns>
    /// <exception cref="MotifStoreUpgradeFailedException">The backup that must precede the deletion failed; nothing was deleted.</exception>
    /// <exception cref="MotifStoreLockException">The creation lock or active-use lease is held by another process.</exception>
    /// <exception cref="IOException">The file could not be read or deleted, for instance while it is open.</exception>
    /// <exception cref="InvalidDataException">The file identity, metadata, or project binding is invalid.</exception>
    public static StoreDeletion DeleteIfOtherVersion(
        string path,
        ProjectLocator project,
        int supportedSchema,
        Version workerVersion,
        TimeSpan? ownershipPatience = null,
        Action? onWaitingForOwnership = null) =>
        MotifSqliteStore.DeleteIfOtherVersion(path, Describe(path, project, supportedSchema, workerVersion),
            ownershipPatience, onWaitingForOwnership, fullPath =>
            {
                var evidence = TrialExportDirectory(fullPath);
                if (Directory.Exists(evidence))
                    Directory.Delete(evidence, recursive: (File.GetAttributes(evidence) & FileAttributes.ReparsePoint) == 0);
            });

    private static MotifSqliteStoreDescriptor Describe(
        string path, ProjectLocator project, int supportedSchema, Version workerVersion,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A database path is required.", nameof(path));
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(project.FullFwDataPath))
            throw new ArgumentException("A full .fwdata path is required.", nameof(project));
        if (string.IsNullOrWhiteSpace(project.FieldWorksProjectIdentity))
            throw new ArgumentException("A FieldWorks project identity is required.", nameof(project));
        ArgumentNullException.ThrowIfNull(workerVersion);
        if (supportedSchema < 1) throw new ArgumentOutOfRangeException(nameof(supportedSchema));
        if (supportedSchema > MotifSchema.CurrentSchema)
            throw new NotSupportedException($"Motif schema {supportedSchema} is not known to this worker.");
        if (workerVersion < MotifSchema.MinimumWorkerVersion(supportedSchema))
            throw new NotSupportedException(
                $"Worker {workerVersion} is older than schema {supportedSchema} minimum " +
                $"{MotifSchema.MinimumWorkerVersion(supportedSchema)}.");

        return new MotifSqliteStoreDescriptor
        {
            Name = "Motif database",
            ApplicationId = MotifSchema.ApplicationId,
            CurrentSchema = supportedSchema,
            IncrementalVacuum = true,
            ValidateSchema = MotifSchema.ValidateSchema,
            Create = (connection, transaction) => MotifSchema.Create(connection, transaction, project, timeProvider),
            // Every job, progress and Proposal write goes through here, so its files stay open between calls.
            PooledConnections = true,
            Upgrade = supportedSchema == MotifSchema.CurrentSchema
                ? (output, _, backup, safety) =>
                    MotifStoreUpgrade.Build(path, output, project, false, backup, workerVersion, safetyBackup: safety)
                : null,
            BackUp = reason =>
            {
                if (reason == StoreBackups.PreUpgrade) RefuseOtherProjectOrNewerWorker(path, project, workerVersion);
                return StoreBackups.WriteHeldExclusively(project, path, reason, timeProvider);
            },
            Retain = safety => StoreBackupRetention.Prune(
                Path.GetDirectoryName(safety.Directory)!, StoreBackups.PreUpgrade, safety.Directory, supportedSchema,
                storePath: path),
            OnWritableOpen = MotifSchema.EnsureStoreId,
            BeforeOpen = connection =>
            {
                var metadata = MotifSchema.ReadMetadata(connection);
                EnsureLocatorMatches(metadata.Project, project, path);
                if (workerVersion < metadata.MinimumWorkerVersion)
                    throw new MotifStoreVersionException(Path.GetFullPath(path),
                        $"Worker {workerVersion} is older than database minimum {metadata.MinimumWorkerVersion}. " +
                        "Install the newer Motif, or restore a pre-upgrade backup with motif store restore.")
                    { Newer = true };
            }
        };
    }

    /// <summary>
    /// Reads the random id written when the store file at a path was created, or null when the file cannot be read
    /// as a current Motif store (absent, locked, another schema, or being created). A copy keeps its source's id.
    /// </summary>
    public static string? TryReadStoreId(string path)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT StoreId FROM MotifStoreIdentity WHERE Id = 1;";
            return command.ExecuteScalar() as string is { Length: > 0 } id ? id : null;
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidOperationException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Returns the store-owned directory for durable Trial evidence.</summary>
    public static string TrialExportDirectory(string storePath) => storePath + ".trial-exports";

    /// <summary>Opens a configured connection while this worker owns the database.</summary>
    public SqliteConnection OpenConnection() => _store.OpenConnection();

    /// <summary>
    /// Says what upgrading an older project store would do, without changing it: no backup, lock, rename or report is written.
    /// The build runs into a scratch file that is deleted before this returns.
    /// </summary>
    /// <param name="path">The existing sibling Motif database path.</param>
    /// <param name="project">The project locator that must match persisted metadata.</param>
    /// <param name="workerVersion">The worker version used for compatibility checks.</param>
    /// <returns>The report the real upgrade would produce, or <see langword="null"/> when the store is already current.</returns>
    public static MotifStoreUpgrade.Report? DryRunUpgrade(string path, ProjectLocator project, Version workerVersion)
    {
        var descriptor = Describe(path, project, MotifSchema.CurrentSchema, workerVersion);
        using var lease = MotifSqliteStore.AcquireExistingReadLease(path);
        // Beside the store, as the real upgrade writes, so a store too large for the temp volume still has its dry run.
        var scratch = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".motif-upgrade-dry-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            using var probe = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = Path.GetFullPath(path), Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 1,
            }.ToString());
            probe.Open();
            using var command = probe.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            var schema = Convert.ToInt32(command.ExecuteScalar());
            if (schema > MotifSchema.CurrentSchema)
                throw new MotifStoreVersionException(Path.GetFullPath(path), 
                    $"This {descriptor.Name} is at schema {schema} and this build understands {MotifSchema.CurrentSchema}. " +
                    "Install the newer Motif, or restore a pre-upgrade backup with motif store restore.")
                { Newer = true };
            if (schema == MotifSchema.CurrentSchema)
            {
                // A store that needs a newer worker is refused here too.
                if (workerVersion < MotifSchema.ReadMetadata(probe).MinimumWorkerVersion)
                    throw new MotifStoreVersionException(Path.GetFullPath(path),
                        $"Worker {workerVersion} is older than database minimum {MotifSchema.ReadMetadata(probe).MinimumWorkerVersion}. " +
                        "Install the newer Motif, or restore a pre-upgrade backup with motif store restore.")
                    { Newer = true };
                return null;
            }
            probe.Close();
            return MotifStoreUpgrade.Build(path, Path.Combine(scratch, "dry-run.db"), project, false, null, workerVersion);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(scratch, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Reads an existing project store without creating it or changing its journal mode.</summary>
    /// <param name="path">The existing sibling Motif database path.</param>
    /// <param name="project">The project locator that must match persisted metadata.</param>
    /// <param name="read">Reads optional display data from one validated read-only transaction.</param>
    /// <returns>The data returned by <paramref name="read"/>.</returns>
    /// <remarks>Store leases fail immediately; entering the read snapshot waits at most one second for SQLite.</remarks>
    public static T ReadExisting<T>(string path, ProjectLocator project, Func<SqliteConnection, SqliteTransaction, T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        var descriptor = Describe(path, project, MotifSchema.CurrentSchema, MotifProductVersion.Current);
        using var lease = MotifSqliteStore.AcquireExistingReadLease(path);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(path),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
            DefaultTimeout = 1,
        }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "PRAGMA application_id;";
            if (Convert.ToInt32(command.ExecuteScalar()) != MotifSchema.ApplicationId)
                throw new InvalidDataException("The file is not a Motif project database.");
            command.CommandText = "PRAGMA user_version;";
            var schema = Convert.ToInt32(command.ExecuteScalar());
            if (schema > 0 && schema < MotifSchema.CurrentSchema)
                throw new MotifStoreVersionException(Path.GetFullPath(path), StoreUpgradeMessages.Format(
                    "OlderStore", descriptor.Name, Path.GetFileName(path), schema, MotifSchema.CurrentSchema))
                { Upgradable = true };
            if (schema != MotifSchema.CurrentSchema)
                throw new NotSupportedException("The project database has another schema version.");
        }
        MotifSchema.ValidateReadSchema(connection, transaction);
        var metadata = MotifSchema.ReadMetadata(connection, transaction);
        EnsureLocatorMatches(metadata.Project, project, path);
        if (MotifProductVersion.Current < metadata.MinimumWorkerVersion)
            throw new NotSupportedException("The project database requires a newer worker.");
        return read(connection, transaction);
    }

    /// <summary>Gets the fully resolved path of the owned Motif database.</summary>
    public string FullPath => _store.FullPath;

    internal int TrackedConnectionCount => _store.TrackedConnectionCount;

    /// <summary>Closes this process's connections. Nothing is released for anyone else.</summary>
    public void Dispose() => _store.Dispose();

    internal static SqliteConnection OpenConfiguredConnectionForTesting(
        string path,
        Action<SqliteConnection> configure) => MotifSqliteStore.OpenConnectionForTesting(path, configure);

    // No backup is written of another project's store, or of one this worker may not read.
    private static void RefuseOtherProjectOrNewerWorker(string path, ProjectLocator project, Version workerVersion)
    {
        using var connection = OpenReadOnlyUnpooled(Path.GetFullPath(path));
        (ProjectLocator Project, Version MinimumWorkerVersion) metadata;
        try
        {
            metadata = MotifSchema.ReadMetadata(connection);
        }
        catch (SqliteException exception)
        {
            throw new InvalidDataException(StoreUpgradeMessages.Get("MissingMetadata"), exception)
            { Data = { [MotifSqliteStore.SourceInvalidKey] = true } };
        }
        EnsureLocatorMatches(metadata.Project, project, path);
        if (workerVersion < metadata.MinimumWorkerVersion)
            throw new MotifStoreVersionException(Path.GetFullPath(path), StoreUpgradeMessages.Get("NewerWorker"))
            { Newer = true };
    }

    private static void EnsureLocatorMatches(ProjectLocator stored, ProjectLocator requested, string path)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(stored.FullFwDataPath, requested.FullFwDataPath) ||
            !StringComparer.Ordinal.Equals(stored.FieldWorksProjectIdentity, requested.FieldWorksProjectIdentity))
            throw StoreProjectMismatch.Create(Path.GetFullPath(path), stored.FullFwDataPath);
    }
}
