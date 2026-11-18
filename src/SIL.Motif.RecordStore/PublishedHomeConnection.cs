using System.Data;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace SIL.Motif.RecordStore;

internal static class PublishedHomeConnection
{
    private const int PersistWal = 10;

    internal static void ConfigureReader(object? sender, StateChangeEventArgs state)
    {
        if (state.CurrentState != ConnectionState.Open || sender is not SqliteConnection connection) return;
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA temp_store=MEMORY";
        command.ExecuteNonQuery();
    }

    internal static void ConfigureWriter(object? sender, StateChangeEventArgs state)
    {
        if (state.CurrentState == ConnectionState.Open && sender is SqliteConnection connection)
            KeepWal(connection);
    }

    internal static void KeepWal(SqliteConnection connection)
    {
        // Persistent WAL keeps both sidecars available between writer connections.
        var persist = 1;
        var result = sqlite3_file_control(connection.Handle!, "main", PersistWal, ref persist);
        if (result != raw.SQLITE_OK) throw new InvalidDataException("SQLite cannot retain this Home's WAL sidecars.");
        using var command = connection.CreateCommand();
        // A final checkpoint can truncate the WAL while preserving both sidecars.
        command.CommandText = "PRAGMA journal_size_limit=0";
        command.ExecuteNonQuery();
    }

    [DllImport("e_sqlite3", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_file_control(sqlite3 database, [MarshalAs(UnmanagedType.LPStr)] string name, int operation, ref int value);
}
