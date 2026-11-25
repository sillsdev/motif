using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>What a store file holds, read without opening it for use.</summary>
/// <param name="SchemaVersion">The store's schema generation, from SQLite's <c>user_version</c>.</param>
public sealed record StoreFileFacts(int SchemaVersion);

public sealed partial class MotifDatabase
{
    /// <summary>
    /// Copies the store at <paramref name="path"/> to <paramref name="destination"/> with SQLite's online backup, so the
    /// copy is one consistent snapshot while other Motif processes read or write the store.
    /// </summary>
    /// <remarks>
    /// The copy holds a shared use lease, the same one every reader holds, so it runs beside readers and writers and
    /// is turned away only while a process is resetting, relocating or restoring the store. The copy is written in rollback
    /// journal mode, so it is one file with no <c>-wal</c> or <c>-shm</c> beside it.
    /// </remarks>
    /// <param name="path">The existing Motif store.</param>
    /// <param name="destination">The file to create; it must not exist.</param>
    /// <returns>The copied store's schema version.</returns>
    /// <exception cref="MotifStoreLockException">Another Motif process holds the store exclusively.</exception>
    /// <exception cref="FileNotFoundException">The store, or its lease file, is absent.</exception>
    /// <exception cref="InvalidDataException">The file is not a Motif store, or is corrupt.</exception>
    /// <exception cref="IOException">SQLite could not read the store, as when it stays locked past the busy timeout.</exception>
    public static StoreFileFacts BackUpExisting(string path, string destination)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("The Motif database does not exist.", fullPath);
        using var lease = MotifSqliteStore.AcquireExistingReadLease(fullPath);
        return CopyOnline(fullPath, destination);
    }

    /// <summary>
    /// Copies a store the caller already holds exclusively, as <see cref="BackUpExisting"/> copies one it shares,
    /// without taking a lease the caller's own exclusive lease would refuse.
    /// </summary>
    public static StoreFileFacts BackUpHeldExclusively(string path, string destination) =>
        CopyOnline(Path.GetFullPath(path), destination);

    /// <summary>Reads a store file's application id and schema, refusing a file that is not a Motif store.</summary>
    /// <exception cref="InvalidDataException">The file is not a Motif store, or is corrupt.</exception>
    public static StoreFileFacts ReadStoreFileFacts(string path)
    {
        try
        {
            using var connection = OpenReadOnlyUnpooled(Path.GetFullPath(path));
            return Inspect(connection);
        }
        catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
        {
            throw new InvalidDataException("The file is corrupt or is not a Motif database.", exception);
        }
        catch (SqliteException exception)
        {
            throw new IOException("The Motif database is unavailable.", exception);
        }
    }

    private static StoreFileFacts CopyOnline(string fullPath, string destination)
    {
        try
        {
            using var source = OpenReadOnlyUnpooled(fullPath);
            var facts = Inspect(source);
            using (var target = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = destination, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false,
            }.ToString()))
            {
                target.Open();
                source.BackupDatabase(target);
                // The page copy keeps the source's WAL header; leave one self-contained file.
                using var command = target.CreateCommand();
                command.CommandText = "PRAGMA journal_mode = DELETE;";
                command.ExecuteNonQuery();
            }
            return facts;
        }
        catch (SqliteException exception) when (SqliteConnections.IsCorruption(exception))
        {
            throw new InvalidDataException("The Motif database is corrupt or is not a database.", exception);
        }
        catch (SqliteException exception)
        {
            throw new IOException("The Motif database is unavailable.", exception);
        }
    }

    private static SqliteConnection OpenReadOnlyUnpooled(string fullPath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = fullPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 30,
        }.ToString());
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    private static StoreFileFacts Inspect(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA application_id;";
        if (Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) != MotifSchema.ApplicationId)
            throw new InvalidDataException("The file is not a Motif project database.");
        command.CommandText = "PRAGMA user_version;";
        var schema = Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        if (schema < 1) throw new InvalidDataException("The Motif project database has not been initialized.");
        return new StoreFileFacts(schema);
    }

    /// <summary>
    /// Runs <paramref name="act"/> while no other Motif process has the store open: under the creation lock and an
    /// exclusive use lease, without opening the store itself. The store file need not exist.
    /// </summary>
    /// <exception cref="MotifStoreLockException">Another Motif process holds the store open.</exception>
    public static T WithExclusiveStoreFiles<T>(string path, TimeSpan? ownershipPatience, Func<T> act)
    {
        ArgumentNullException.ThrowIfNull(act);
        return MotifSqliteStore.WithExclusiveFiles(path, ownershipPatience, act);
    }
}
