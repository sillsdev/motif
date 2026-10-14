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
public sealed class MotifDatabase : IDisposable
{
    private readonly MotifSqliteStore _store;

    private MotifDatabase(MotifSqliteStore store) => _store = store;

    /// <summary>
    /// Opens a project database with a shared active-use lease, creating it if absent. An existing database at
    /// any other schema is refused rather than migrated: pre-1.0 Motif has no upgrade path.
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
        return new MotifDatabase(MotifSqliteStore.Open(path, descriptor, ownershipPatience));
    }

    /// <summary>
    /// Deletes a project database that <see cref="OpenOwned"/> would refuse with a
    /// <see cref="MotifStoreVersionException"/>, because another version of Motif made it. Any other refusal is
    /// thrown as <see cref="OpenOwned"/> throws it, and nothing is deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The guarantee is that this never deletes a database this version of Motif can use. A database refused as
    /// another version's is never opened or written by this version, because <see cref="OpenOwned"/> refuses it,
    /// and it is never migrated. So the only change that can turn the file at this path into a usable database is
    /// creation, and creation holds the creation lock this method decides and deletes under. A database created
    /// while this waits for that lock is found usable and kept, pinned by
    /// `ADatabaseCreatedWhileTheDeleteWaitsIsKept`; a delete that gets the lock first deletes, and the next open
    /// creates a fresh database, pinned by `ADeleteThatWinsTheLockIsFollowedByACleanRecreate`.
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
    /// <returns><c>true</c> when the file was deleted; <c>false</c> when it is absent, not yet created, or usable.</returns>
    /// <exception cref="MotifStoreLockException">The creation lock or active-use lease is held by another process.</exception>
    /// <exception cref="IOException">The file could not be read or deleted, for instance while it is open.</exception>
    /// <exception cref="InvalidDataException">The file identity, metadata, or project binding is invalid.</exception>
    public static bool DeleteIfOtherVersion(
        string path,
        ProjectLocator project,
        int supportedSchema,
        Version workerVersion,
        TimeSpan? ownershipPatience = null,
        Action? onWaitingForOwnership = null) =>
        MotifSqliteStore.DeleteIfOtherVersion(path, Describe(path, project, supportedSchema, workerVersion),
            ownershipPatience, onWaitingForOwnership);

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
            ValidateSchema = MotifSchema.ValidateSchema,
            Create = (connection, transaction) => MotifSchema.Create(connection, transaction, project, timeProvider),
            // Every job, progress and Proposal write goes through here, so its files stay open between calls.
            PooledConnections = true,
            BeforeOpen = connection =>
            {
                var metadata = MotifSchema.ReadMetadata(connection);
                EnsureLocatorMatches(metadata.Project, project);
                if (workerVersion < metadata.MinimumWorkerVersion)
                    throw new MotifStoreVersionException(Path.GetFullPath(path),
                        $"Worker {workerVersion} is older than database minimum {metadata.MinimumWorkerVersion}.");
            }
        };
    }

    /// <summary>Opens a configured connection while this worker owns the database.</summary>
    public SqliteConnection OpenConnection() => _store.OpenConnection();

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
        using var lease = new FileStream(path + ".use.lock", FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
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
            if (Convert.ToInt32(command.ExecuteScalar()) != MotifSchema.CurrentSchema)
                throw new NotSupportedException("The project database has another schema version.");
        }
        descriptor.ValidateSchema(connection, transaction);
        var metadata = MotifSchema.ReadMetadata(connection, transaction);
        EnsureLocatorMatches(metadata.Project, project);
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

    private static void EnsureLocatorMatches(ProjectLocator stored, ProjectLocator requested)
    {
        if (!StringComparer.OrdinalIgnoreCase.Equals(stored.FullFwDataPath, requested.FullFwDataPath) ||
            !StringComparer.Ordinal.Equals(stored.FieldWorksProjectIdentity, requested.FieldWorksProjectIdentity))
            throw new InvalidDataException("The database is registered to a different project locator.");
    }
}
