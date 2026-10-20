using Microsoft.Data.Sqlite;

namespace SIL.Motif.Host.Store;

/// <summary>
/// The SQLite connection settings and failure classification shared by every database Motif owns.
/// </summary>
/// <remarks>
/// A schema module owns its DDL, generation number, migration ladder and <c>ValidateSchema</c>; none of
/// that belongs here. What belongs here is what is true of a SQLite connection regardless of which schema
/// it happens to hold — WAL, the busy timeout, and which storage engine error codes mean the file itself
/// is unreadable. <see cref="MotifSchema"/> and <see cref="MachineSchema"/> both configure their
/// connections through this module so the two databases cannot drift on any of it.
/// </remarks>
public static class SqliteConnections
{
    /// <summary>The connection busy timeout used for short-lived worker database sessions.</summary>
    public const int BusyTimeoutMilliseconds = 15000;

    internal static void ConfigureConnection(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "PRAGMA journal_mode = WAL; PRAGMA busy_timeout = " + BusyTimeoutMilliseconds + "; PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    internal static void ConfigureSession(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout = " + BusyTimeoutMilliseconds + "; PRAGMA foreign_keys = ON;";
        command.ExecuteNonQuery();
    }

    internal static void EnableWal(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL;";
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Whether <paramref name="exception"/> is SQLite reporting another connection's lock (BUSY or LOCKED) after the
    /// busy timeout ran out, which a step that has saved nothing may refuse as retryable.
    /// </summary>
    public static bool IsBusy(Exception exception) => exception is SqliteException { SqliteErrorCode: 5 or 6 };

    internal static bool IsCorruption(SqliteException exception) => IsCorruptionCode(exception.SqliteErrorCode);

    internal static bool IsCorruptionCode(int errorCode) => errorCode is 11 or 26;

    /// <summary>
    /// Whether the SQLite error code is one of PERM, BUSY, LOCKED, READONLY, IOERR, FULL or CANTOPEN.
    /// </summary>
    internal static bool IsStorageUnavailableCode(int errorCode) => errorCode is 3 or 5 or 6 or 8 or 10 or 13 or 14;
}
