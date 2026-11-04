using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class MotifPooledConnectionTransactionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-pool-tx-" + Guid.NewGuid().ToString("N"));

    public MotifPooledConnectionTransactionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void AConnectionClosedInsideARawTransactionIsNotHandedOutStillInIt()
    {
        using var owner = Open("raw.fwdata");
        using (var leaker = owner.OpenConnection())
            Exec(leaker, "BEGIN;");

        using var next = owner.OpenConnection();
        using var transaction = next.BeginTransaction(deferred: true);
    }

    [Fact]
    public void AConnectionOwnerDisposedInsideATransactionIsNotHandedOutStillInIt()
    {
        var owner = Open("owner-dispose.fwdata");
        var leaker = owner.OpenConnection();
        leaker.BeginTransaction(deferred: true);
        owner.Dispose();

        using var second = Open("owner-dispose.fwdata");
        for (var i = 0; i < 5; i++)
        {
            using var next = second.OpenConnection();
            using var transaction = next.BeginTransaction(deferred: true);
        }
    }

    [Fact]
    public async Task OverlappingOwnersOnOnePathNeverShareATransaction()
    {
        using var keeper = Open("overlap.fwdata");
        var failures = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var workers = Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            var random = new Random(worker);
            for (var i = 0; i < 200; i++)
            {
                try
                {
                    var owner = Open("overlap.fwdata");
                    var connection = owner.OpenConnection();
                    var transaction = connection.BeginTransaction(deferred: random.Next(2) == 0);
                    if (random.Next(3) == 0) { owner.Dispose(); continue; }
                    if (random.Next(2) == 0) transaction.Dispose();
                    connection.Dispose();
                    owner.Dispose();
                }
                catch (Exception exception) { failures.Enqueue(exception); }
            }
        })).ToArray();
        await Task.WhenAll(workers);
        Assert.Empty(failures.Select(f => f.Message).Distinct());
        using var after = keeper.OpenConnection();
        using var tx = after.BeginTransaction(deferred: true);
    }

    private MotifDatabase Open(string fileName) => MotifDatabase.OpenOwned(
        Path.Combine(_root, Path.GetFileNameWithoutExtension(fileName) + ".motif.db"),
        new ProjectLocator(Path.Combine(_root, fileName), "new"), MotifSchema.CurrentSchema, new Version(1, 0));

    private static void Exec(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
