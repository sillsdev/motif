using System.Threading;
using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>
/// What one schema supplies so <see cref="MotifSqliteStore"/> can open, create and validate a database
/// whose shape it does not itself know.
/// </summary>
/// <remarks>
/// <see cref="ValidateSchema"/> and <see cref="Create"/> close over whatever else that schema needs —
/// <see cref="MotifSchema.Create"/> closes over the requesting <c>ProjectLocator</c>, for instance — so
/// this descriptor only has to carry the four things that are not already fixed at the call site: the
/// application id, the schema this build requires, schema validation and one-step creation, and an
/// optional extra check on an existing database's identity before it is handed back.
/// </remarks>
internal sealed class MotifSqliteStoreDescriptor
{
    /// <summary>What to call this database when refusing it, so a user with both can tell them apart.</summary>
    public required string Name { get; init; }

    /// <summary>The SQLite <c>application_id</c> this schema's databases are stamped with.</summary>
    public required int ApplicationId { get; init; }

    /// <summary>The schema this build requires: an empty file is created at it, anything else is refused.</summary>
    public required int CurrentSchema { get; init; }

    /// <summary>Validates that an existing database has exactly <see cref="CurrentSchema"/>'s shape.</summary>
    public required Action<SqliteConnection, SqliteTransaction?> ValidateSchema { get; init; }

    /// <summary>Builds every table, index and identity row a brand-new database needs, in one step.</summary>
    public required Action<SqliteConnection, SqliteTransaction?> Create { get; init; }

    /// <summary>
    /// Runs once schema validation passes on an existing database, before it is handed back. <see
    /// cref="MotifSchema"/> uses this to reject a database registered to a different project or too old a
    /// worker; <see cref="MachineSchema"/> has nothing to check here and leaves it null.
    /// </summary>
    public Action<SqliteConnection>? BeforeOpen { get; init; }

    /// <summary>
    /// Keeps closed connections pooled for the store's lifetime instead of closing their files.
    /// </summary>
    /// <remarks>
    /// When the last unpooled connection closes, SQLite checkpoints and deletes the <c>-wal</c> and <c>-shm</c>
    /// files, and the next open creates them again. Where Windows Defender scans, that costs about 12 ms per
    /// repository call against about 2 ms pooled. A pooled store holds its files open until it is disposed, so a
    /// database whose deletion between calls must be noticed, as the machine database's is, stays unpooled.
    /// </remarks>
    public bool PooledConnections { get; init; }

    /// <summary>Enables incremental reclamation when creating a new database.</summary>
    public bool IncrementalVacuum { get; init; }

    public Action<string, int, string>? Upgrade { get; init; }
}

/// <summary>
/// The open-and-create ceremony and connection lifecycle shared by every database Motif owns — a
/// project's Motif database and the machine store alike. <see cref="MotifDatabase"/> and
/// <see cref="MachineDatabase"/> are thin wrappers around this: each supplies its own
/// <see cref="MotifSqliteStoreDescriptor"/> and keeps its own public factory and argument checks, and this
/// module does the rest identically for both.
/// </summary>
/// <remarks>
/// <para>
/// Each open holds a shared use lease for the object's lifetime. Other Motif processes can take the same
/// lease, while reset needs an exclusive lease before it deletes an incompatible database. The separate
/// ownership lock is taken only while a fresh database is being created, because creation is the one
/// operation two processes must never interleave.
/// </para>
/// <para>
/// Creation itself runs inside <c>BEGIN IMMEDIATE</c>/<c>COMMIT</c>, with an explicit <c>ROLLBACK</c> on
/// failure, so a process that dies mid-creation leaves the file empty rather than half built — the next
/// opener sees an ordinary, uncreated database and tries again. A project schema may supply an upgrade
/// callback; other schemas refuse an existing database at a different version.
/// </para>
/// </remarks>
internal sealed class MotifSqliteStore : IDisposable
{
    private static readonly TimeSpan DefaultOwnershipPatience = TimeSpan.FromSeconds(30);
    private static readonly object PoolOwnersGate = new();
    private static readonly Dictionary<string, int> PoolOwnerCounts = new(StringComparer.Ordinal);
    private readonly string _path;
    private readonly string _name;
    private readonly FileStream _useLease;
    private readonly object _stateGate = new();
    private readonly HashSet<SqliteConnection> _connections = [];
    private readonly bool _pooled;
    private readonly string? _poolConnectionString;
    private bool _disposed;

    private MotifSqliteStore(string path, string name, FileStream useLease, bool pooled)
    {
        _path = path;
        _name = name;
        _useLease = useLease;
        _pooled = pooled;
        _poolConnectionString = pooled ? ConnectionString(path, pooled: true) : null;
        if (_poolConnectionString is not null) RegisterPoolOwner(_poolConnectionString);
    }

    /// <summary>Opens a store with a shared use lease, creating its schema if absent.</summary>
    /// <param name="path">The database file path.</param>
    /// <param name="descriptor">The schema and identity rules for the database.</param>
    /// <param name="ownershipPatience">Maximum wait for store locks; defaults to 30 seconds.</param>
    public static MotifSqliteStore Open(string path, MotifSqliteStoreDescriptor descriptor,
        TimeSpan? ownershipPatience = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        FileStream? useLease = null;
        FileStream? ownership = null;
        try
        {
            if (descriptor.Upgrade is not null) PrepareUpgrade(fullPath, descriptor, ownershipPatience ?? DefaultOwnershipPatience);
            useLease = AcquireUseLease(fullPath, ownershipPatience ?? DefaultOwnershipPatience);
            // This orders first creation without excluding other readers.
            if (NeedsCreation(fullPath))
                ownership = AcquireOwnership(fullPath, ownershipPatience ?? DefaultOwnershipPatience);
            using var connection = OpenInspectionConnection(fullPath);
            var schema = Inspect(connection, descriptor, fullPath);

            if (schema == 0)
            {
                if (descriptor.IncrementalVacuum) Execute(connection, "PRAGMA auto_vacuum = INCREMENTAL;");
                Execute(connection, "BEGIN IMMEDIATE;");
                try
                {
                    SetApplicationId(connection, descriptor.ApplicationId);
                    descriptor.Create(connection, null);
                    descriptor.ValidateSchema(connection, null);
                    SetUserVersion(connection, null, descriptor.CurrentSchema);
                    Execute(connection, "COMMIT;");
                }
                catch
                {
                    try { Execute(connection, "ROLLBACK;"); }
                    catch (SqliteException) { }
                    throw;
                }
            }
            else
            {
                ValidateExisting(connection, descriptor);
            }

            SqliteConnections.EnableWal(connection);
            ownership?.Dispose();
            ownership = null;
            var store = new MotifSqliteStore(fullPath, descriptor.Name, useLease, descriptor.PooledConnections);
            useLease = null;
            return store;
        }
        catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
        {
            ownership?.Dispose();
            useLease?.Dispose();
            throw new InvalidDataException($"The {descriptor.Name} is corrupt or is not a database.", exception);
        }
        catch (SqliteException exception)
        {
            ownership?.Dispose();
            useLease?.Dispose();
            throw new IOException($"The {descriptor.Name} is unavailable.", exception);
        }
        catch
        {
            ownership?.Dispose();
            useLease?.Dispose();
            throw;
        }
    }

    private static void PrepareUpgrade(string path, MotifSqliteStoreDescriptor descriptor, TimeSpan patience)
    {
        var pending = path + ".upgrading";
        if (!File.Exists(path) && !File.Exists(pending)) return;
        if (File.Exists(path))
        {
            try
            {
                using var initial = OpenReadOnly(path);
                var version = PragmaInt(initial, "user_version");
                if (version == 0 || version > descriptor.CurrentSchema) return;
                if (version == descriptor.CurrentSchema &&
                    (!File.Exists(pending + ".upgrade.json") || File.Exists(pending) || StoreUpgradeContext.IsSuppressed)) return;
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 14) { }
        }
        using var ownership = AcquireOwnership(path, patience);
        if (File.Exists(path))
        {
            using var current = OpenReadOnly(path);
            var version = PragmaInt(current, "user_version");
            if (version == 0 || version > descriptor.CurrentSchema ||
                version == descriptor.CurrentSchema &&
                (!File.Exists(pending + ".upgrade.json") || File.Exists(pending) || StoreUpgradeContext.IsSuppressed)) return;
        }
        using var lease = AcquireExclusiveUseLease(path);
        if (!File.Exists(path))
        {
            if (StoreUpgradeContext.IsSuppressed)
                throw new MotifStoreVersionException(path, StoreUpgradeMessages.Get("Suppressed"));
            var reportFile = pending + ".upgrade.json";
            if (!File.Exists(pending) || !File.Exists(reportFile))
                throw new InvalidDataException(StoreUpgradeMessages.Get("RecoveryFailed"));
            var report = System.Text.Json.JsonSerializer.Deserialize<MotifStoreUpgrade.Report>(File.ReadAllText(reportFile))
                ?? throw new InvalidDataException(StoreUpgradeMessages.Get("ReportInvalid"));
            if (report.BackupPath is null || !File.Exists(report.BackupPath))
                throw new InvalidDataException(StoreUpgradeMessages.Get("BackupMissing"));
            MoveSidecars(path, report.BackupPath);
            File.Move(pending, path);
            File.Move(reportFile, path + ".upgrade.json", true);
            return;
        }
        int schema;
        using (var connection = OpenReadOnly(path))
        {
            schema = PragmaInt(connection, "user_version");
            if (schema == descriptor.CurrentSchema && !StoreUpgradeContext.IsSuppressed &&
                !File.Exists(pending) && File.Exists(pending + ".upgrade.json"))
            {
                var report = System.Text.Json.JsonSerializer.Deserialize<MotifStoreUpgrade.Report>(
                    File.ReadAllText(pending + ".upgrade.json"));
                if (report?.BackupPath is not null && File.Exists(report.BackupPath))
                    File.Move(pending + ".upgrade.json", path + ".upgrade.json", true);
            }
            if (schema == 0 || schema >= descriptor.CurrentSchema) return;
            if (PragmaInt(connection, "application_id") != descriptor.ApplicationId)
                throw new InvalidDataException($"The file is not a {descriptor.Name}.");
        }
        if (StoreUpgradeContext.IsSuppressed)
            throw new MotifStoreVersionException(path, StoreUpgradeMessages.Get("Suppressed"));
        var backup = path + ".schema" + schema + ".bak";
        if (File.Exists(backup)) throw new IOException(StoreUpgradeMessages.Get("BackupExists"));
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm", ".upgrade.json" })
            if (File.Exists(pending + suffix)) File.Delete(pending + suffix);
        descriptor.Upgrade!(pending, schema, backup);
        File.Move(path, backup);
        MoveSidecars(path, backup);
        File.Move(pending, path);
        File.Move(pending + ".upgrade.json", path + ".upgrade.json", true);
    }

    private static void MoveSidecars(string path, string backup)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
            if (File.Exists(path + suffix)) File.Move(path + suffix, backup + suffix);
    }

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = new Uri(path).AbsoluteUri, Mode = SqliteOpenMode.ReadOnly, Pooling = false
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    /// <summary>
    /// Deletes the store if <see cref="Open"/> would refuse it with a <see cref="MotifStoreVersionException"/>,
    /// deciding and deleting under the creation lock, the one lock that orders against the store's creation.
    /// </summary>
    /// <returns><c>true</c> when the file was deleted; <c>false</c> when it is absent, empty, or usable.</returns>
    public static bool DeleteIfOtherVersion(string path, MotifSqliteStoreDescriptor descriptor,
        TimeSpan? ownershipPatience = null, Action? onWaitingForOwnership = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        var fullPath = Path.GetFullPath(path);
        using var ownership = AcquireOwnership(fullPath, ownershipPatience ?? DefaultOwnershipPatience,
            onWaitingForOwnership);
        if (!File.Exists(fullPath)) return false;
        try
        {
            using var connection = OpenInspectionConnection(fullPath);
            var schema = PragmaInt(connection, "user_version");
            if (descriptor.Upgrade is not null && schema > 0 && schema < descriptor.CurrentSchema &&
                PragmaInt(connection, "application_id") == descriptor.ApplicationId) return false;
            if (Inspect(connection, descriptor, fullPath) != 0) ValidateExisting(connection, descriptor);
            return false;
        }
        catch (MotifStoreVersionException)
        {
        }
        catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
        {
            throw new InvalidDataException($"The {descriptor.Name} is corrupt or is not a database.", exception);
        }
        catch (SqliteException exception)
        {
            throw new IOException($"The {descriptor.Name} is unavailable.", exception);
        }

        using var useLease = AcquireExclusiveUseLease(fullPath);
        File.Delete(fullPath);
        return true;
    }

    public SqliteConnection OpenConnection()
    {
        lock (_stateGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MotifSqliteStore));
            SqliteConnection connection;
            try
            {
                connection = OpenConfiguredConnection(_path, _pooled, RemoveConnection);
            }
            catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
            {
                throw new InvalidDataException($"The {_name} is corrupt or is not a database.", exception);
            }
            catch (SqliteException exception)
            {
                throw new IOException($"The {_name} is unavailable.", exception);
            }
            _connections.Add(connection);
            return connection;
        }
    }

    public string FullPath => _path;

    public int TrackedConnectionCount
    {
        get
        {
            lock (_stateGate) return _connections.Count;
        }
    }

    public void Dispose()
    {
        List<SqliteConnection> connections;
        lock (_stateGate)
        {
            if (_disposed) return;
            _disposed = true;
            connections = [.. _connections];
            _connections.Clear();
        }

        try
        {
            foreach (var connection in connections)
            {
                if (connection is OwnedSqliteConnection owned)
                    owned.DisposeFromOwner();
                else
                    connection.Dispose();
            }
        }
        finally
        {
            try
            {
                if (_poolConnectionString is not null) ReleasePoolOwner(_poolConnectionString);
            }
            finally
            {
                _useLease.Dispose();
            }
        }
    }

    internal static SqliteConnection OpenConnectionForTesting(string path, Action<SqliteConnection> configure) =>
        OpenConnectionCore(path, configure);

    private static SqliteConnection OpenInspectionConnection(string path)
        => OpenConnectionCore(path, SqliteConnections.ConfigureSession);

    private static SqliteConnection OpenConfiguredConnection(
        string path,
        bool pooled,
        Action<SqliteConnection>? onDisposed = null)
        => OpenConnectionCore(path, SqliteConnections.ConfigureConnection, onDisposed, pooled);

    private static SqliteConnection OpenConnectionCore(
        string path,
        Action<SqliteConnection> configure,
        Action<SqliteConnection>? onDisposed = null,
        bool pooled = false)
    {
        var connectionString = ConnectionString(path, pooled);
        var connection = onDisposed is null
            ? new SqliteConnection(connectionString)
            : new OwnedSqliteConnection(connectionString, onDisposed);
        try
        {
            if (HasShortTestTimeout(path)) connection.DefaultTimeout = 1;
            connection.Open();
            configure(connection);
            if (HasShortTestTimeout(path))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA busy_timeout = 100;";
                command.ExecuteNonQuery();
            }
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static string ConnectionString(string path, bool pooled) => new SqliteConnectionStringBuilder
    {
        // Opening the main file through a URI enables read-only URI ATTACH elsewhere in this process.
        DataSource = new Uri(path).AbsoluteUri,
        Pooling = pooled,
        Mode = SqliteOpenMode.ReadWriteCreate
    }.ToString();

    private static bool HasShortTestTimeout(string path)
    {
        var testPath = AmbientEnvironment.Get("MOTIF_TEST_SQLITE_SHORT_TIMEOUT_FOR");
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return !string.IsNullOrWhiteSpace(testPath) &&
            string.Equals(Path.GetFullPath(testPath), Path.GetFullPath(path), comparison);
    }

    private static void RegisterPoolOwner(string connectionString)
    {
        lock (PoolOwnersGate)
        {
            PoolOwnerCounts.TryGetValue(connectionString, out var count);
            PoolOwnerCounts[connectionString] = count + 1;
        }
    }

    private static void ReleasePoolOwner(string connectionString)
    {
        lock (PoolOwnersGate)
        {
            if (!PoolOwnerCounts.TryGetValue(connectionString, out var count))
                throw new InvalidOperationException("A pooled Motif store has no registered owner.");
            if (count > 1)
            {
                PoolOwnerCounts[connectionString] = count - 1;
                return;
            }

            try
            {
                // Clear the pool before releasing the last owner's use lease.
                using var pooled = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(pooled);
            }
            finally
            {
                PoolOwnerCounts.Remove(connectionString);
            }
        }
    }

    private void RemoveConnection(SqliteConnection connection)
    {
        lock (_stateGate)
            _connections.Remove(connection);
    }

    private sealed class OwnedSqliteConnection : SqliteConnection
    {
        private readonly Action<SqliteConnection> _onDisposed;
        private bool _disposedByOwner;

        public OwnedSqliteConnection(string connectionString, Action<SqliteConnection> onDisposed)
            : base(connectionString) => _onDisposed = onDisposed;

        protected override void Dispose(bool disposing)
        {
            try { base.Dispose(disposing); }
            finally
            {
                if (disposing) _onDisposed(this);
            }
        }

        public void DisposeFromOwner()
        {
            _disposedByOwner = true;
            Dispose();
        }

        public override void Open()
        {
            if (_disposedByOwner) throw new ObjectDisposedException(nameof(OwnedSqliteConnection));
            base.Open();
        }
    }

    /// Throws for a file that is not this store at this build's schema; returns its schema, 0 before creation.
    private static int Inspect(SqliteConnection connection, MotifSqliteStoreDescriptor descriptor, string fullPath)
    {
        var applicationId = PragmaInt(connection, "application_id");
        var schema = PragmaInt(connection, "user_version");
        if (applicationId != 0 && applicationId != descriptor.ApplicationId)
            throw new InvalidDataException($"The file is not a {descriptor.Name}.");
        if (schema < 0)
            throw new InvalidDataException($"The {descriptor.Name} has an invalid schema generation.");
        if (schema > descriptor.CurrentSchema)
            throw new MotifStoreVersionException(fullPath,
                $"This {descriptor.Name} is at schema {schema} and this build understands " +
                $"{descriptor.CurrentSchema}. " +
                "Something newer opened it; update Motif and try again.");

        var hasTables = HasUserTables(connection);
        if (applicationId == 0 && schema != 0)
            throw new InvalidDataException($"A {descriptor.Name} with a schema must have its application id.");
        if (schema == 0 && hasTables)
            throw new InvalidDataException($"The existing database has no registered {descriptor.Name} schema.");

        // Without an upgrade callback, there is no safe way to open an older schema.
        if (schema != 0 && schema != descriptor.CurrentSchema)
            throw new MotifStoreVersionException(fullPath,
                StoreUpgradeMessages.Format("UnsupportedSchema", descriptor.Name, fullPath, schema, descriptor.CurrentSchema));
        return schema;
    }

    private static void ValidateExisting(SqliteConnection connection, MotifSqliteStoreDescriptor descriptor)
    {
        descriptor.ValidateSchema(connection, null);
        descriptor.BeforeOpen?.Invoke(connection);
    }

    /// True when the file is absent or empty, so it still needs its schema created.
    private static bool NeedsCreation(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            using var connection = OpenInspectionConnection(path);
            return PragmaInt(connection, "user_version") == 0;
        }
        catch (SqliteException)
        {
            return true;
        }
    }

    // Waits rather than fails: two processes opening a database that needs migrating is ordinary.
    private static FileStream AcquireOwnership(string path, TimeSpan patience, Action? onWaiting = null)
    {
        var deadline = DateTime.UtcNow.Add(patience);
        while (true)
        {
            try { return AcquireOwnershipCore(path); }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                onWaiting?.Invoke();
                onWaiting = null;
                Thread.Sleep(25);
            }
        }
    }

    /// <summary>Acquires a database's ownership file for a platform-specific lock test.</summary>
    internal static FileStream AcquireOwnershipForTesting(string path, TimeSpan patience) =>
        AcquireOwnership(path, patience);

    private static FileStream AcquireUseLease(string path, TimeSpan patience)
    {
        var deadline = DateTime.UtcNow.Add(patience);
        while (true)
        {
            try
            {
                // Shared access modes let every opener coexist and become shared Unix file locks.
                return new FileStream(path + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);
            }
            catch (IOException exception) when (IsOwnershipLockContention(exception))
            {
                if (DateTime.UtcNow >= deadline)
                    throw new MotifStoreLockException("Another Motif process is resetting this database.", exception);
                Thread.Sleep(25);
            }
        }
    }

    private static FileStream AcquireExclusiveUseLease(string path)
    {
        try
        {
            return new FileStream(path + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
                FileShare.None, 1, FileOptions.None);
        }
        catch (IOException exception) when (IsOwnershipLockContention(exception))
        {
            throw new MotifStoreLockException("Another Motif process has this database open.", exception);
        }
    }

    private static FileStream AcquireOwnershipCore(string path)
    {
        var lockPath = path + ".owner.lock";
        FileStream stream;
        try
        {
            stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                OperatingSystem.IsWindows() ? FileOptions.DeleteOnClose : FileOptions.None);
        }
        catch (IOException exception) when (IsOwnershipLockContention(exception))
        {
            throw new MotifStoreLockException("Another Motif worker owns this database.", exception);
        }

        try
        {
            if (OperatingSystem.IsWindows()) stream.Lock(0, 1);
            return stream;
        }
        catch (IOException exception) when (IsOwnershipLockContention(exception))
        {
            stream.Dispose();
            throw new MotifStoreLockException("Another Motif worker owns this database.", exception);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static bool IsOwnershipLockContention(IOException exception)
    {
        var errorCode = exception.HResult & 0xffff;
        return exception.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021) ||
            errorCode is 11 or 35;
    }

    private static bool HasUserTables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('table', 'index', 'view', 'trigger') " +
            "AND name NOT LIKE 'sqlite_%';";
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static void SetApplicationId(SqliteConnection connection, int applicationId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA application_id = {applicationId};";
        command.ExecuteNonQuery();
    }

    private static int PragmaInt(SqliteConnection connection, string pragma)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA {pragma};";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void SetUserVersion(SqliteConnection connection, SqliteTransaction? transaction, int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
