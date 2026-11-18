using Microsoft.Data.Sqlite;

namespace SIL.Motif.RecordStore;

internal static class RecordSqlite
{
    // Temporary stores and backups need their native handles released at disposal.
    internal static string ConnectionString(string path, SqliteOpenMode mode = SqliteOpenMode.ReadWriteCreate) =>
        new SqliteConnectionStringBuilder { DataSource = path, Mode = mode, Pooling = false }.ToString();

    internal static void BackupTo(SqliteConnection source, string destinationPath, Action<SqliteConnection>? configureDestination = null)
    {
        using var destination = new SqliteConnection(ConnectionString(destinationPath));
        destination.Open();
        configureDestination?.Invoke(destination);
        source.BackupDatabase(destination);
    }
}
