using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class MotifSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.MotifSchemaTests",
        Guid.NewGuid().ToString("N"));

    public MotifSchemaTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void TheSchemaHasNoDecisionsTable()
    {
        using var database = Open();
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Decisions';";

        Assert.Equal(0L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void AStoreWithADecisionsTableIsRefusedNotMigrated()
    {
        using (var database = Open())
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE Decisions (ProposalId TEXT NOT NULL);";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => Open().Dispose());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private MotifDatabase Open() => MotifDatabase.OpenOwned(Path.Combine(_root, "motif.db"),
        new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project-identity"),
        MotifSchema.CurrentSchema, new Version(1, 0));
}
