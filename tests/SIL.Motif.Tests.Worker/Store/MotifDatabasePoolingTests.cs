using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class MotifDatabasePoolingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-database-pooling-" + Guid.NewGuid().ToString("N"));

    public MotifDatabasePoolingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void DisposingOneOwnerKeepsTheOtherOwnersPooledConnectionUntilLastOwnerDisposes()
    {
        var path = Path.Combine(_root, "shared.motif.db");
        var project = new ProjectLocator(Path.Combine(_root, "shared.fwdata"), "shared-project");
        using var firstOwner = MotifDatabase.OpenOwned(
            path, project, MotifSchema.CurrentSchema, new Version(1, 0));
        using var secondOwner = MotifDatabase.OpenOwned(
            path, project, MotifSchema.CurrentSchema, new Version(1, 0));

        const int connectionCacheSize = 12345;
        using (var connection = secondOwner.OpenConnection())
        {
            Execute(connection, $"PRAGMA cache_size = {connectionCacheSize};");
            Assert.Equal(connectionCacheSize, Scalar(connection, "PRAGMA cache_size;"));
        }

        firstOwner.Dispose();

        using (var connection = secondOwner.OpenConnection())
        {
            Assert.Equal(connectionCacheSize, Scalar(connection, "PRAGMA cache_size;"));
            Assert.Equal("shared-project", ScalarText(connection,
                "SELECT FieldWorksProjectIdentity FROM MotifMetadata WHERE Id = 1;"));
        }

        secondOwner.Dispose();
        File.Delete(path);

        Assert.False(File.Exists(path));
        Assert.False(File.Exists(path + "-wal"));
        Assert.False(File.Exists(path + "-shm"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    private static int Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static string? ScalarText(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar() as string;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
