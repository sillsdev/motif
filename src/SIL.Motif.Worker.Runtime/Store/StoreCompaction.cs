using System.Globalization;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Worker.Store;

/// <summary>Rewrites a project store to give back free pages, when that is worth its disk and time.</summary>
public static class StoreCompaction
{
    /// <summary>The free bytes inside the file below which a full rewrite is not worth its disk and time.</summary>
    public const long MinimumFreeBytes = 16L * 1024 * 1024;

    /// <summary>Compacts the store while this process holds the project's parse lease; false when it was skipped.</summary>
    /// <remarks>
    /// Write-ahead mode writes the new copy through the log, so the check asks for two copies of the used pages.
    /// </remarks>
    public static bool CompactWhenWorthwhile(MotifDatabase database, ProjectParseLease lease)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(lease);
        using var connection = database.OpenConnection();
        return CompactWhenWorthwhile(connection, database.FullPath);
    }

    /// <summary>Compacts the store behind an open connection; the caller holds the project's parse lease.</summary>
    internal static bool CompactWhenWorthwhile(SqliteConnection connection, string path)
    {
        try
        {
            long free;
            long size;
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT freelist_count * page_size, page_count * page_size FROM pragma_freelist_count, pragma_page_size, pragma_page_count;";
                using var reader = command.ExecuteReader();
                if (!reader.Read()) return false;
                free = reader.GetInt64(0);
                size = reader.GetInt64(1);
            }
            if (free < MinimumFreeBytes || free * 4 < size) return false;
            var used = size - free;
            var copies = Journal(connection).Equals("wal", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (root is null || new DriveInfo(root).AvailableFreeSpace < used * copies + MinimumFreeBytes) return false;
            using var busy = connection.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout = 0;";
            busy.ExecuteNonQuery();
            using var vacuum = connection.CreateCommand();
            vacuum.CommandText = "PRAGMA auto_vacuum = INCREMENTAL; VACUUM;";
            vacuum.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6 or 13)
        {
            return false;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Journal(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }
}
