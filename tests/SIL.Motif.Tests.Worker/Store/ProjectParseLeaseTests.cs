using System.Diagnostics;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class ProjectParseLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-parse-lease-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void IndependentConnectionsAdmitOnlyOneOwnerAndReleaseOnDispose()
    {
        using var first = Open();
        using var second = Open();
        using (var lease = ProjectParseLease.TryAcquire(first))
        {
            Assert.NotNull(lease);
            Assert.Null(ProjectParseLease.TryAcquire(second));
        }
        using var next = ProjectParseLease.TryAcquire(second);
        Assert.NotNull(next);
    }

    [Fact]
    public void APidReusedByAnotherProcessDoesNotKeepACrashedCallersAdmission()
    {
        using var database = Open();
        using (var connection = database.OpenConnection())
        {
            using var process = Process.GetCurrentProcess();
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ActiveParse VALUES (1, 'abandoned', $pid, $start);";
            insert.Parameters.AddWithValue("$pid", process.Id);
            insert.Parameters.AddWithValue("$start", "another-process-start");
            insert.ExecuteNonQuery();
        }
        using var recovered = ProjectParseLease.TryAcquire(database);
        Assert.NotNull(recovered);
    }

    private MotifDatabase Open()
    {
        Directory.CreateDirectory(_root);
        return MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"),
            new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project"), MotifSchema.CurrentSchema, new Version(1, 0));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
