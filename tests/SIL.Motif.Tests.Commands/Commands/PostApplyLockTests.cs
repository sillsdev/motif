using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Host;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A real second connection holding the store's write lock, against the post-apply retry and the Before Assessment binding.</summary>
[Collection(LcmCacheParallelCollections.Group2)]
public sealed class PostApplyLockTests(PristineProjectFixture pristine) : IDisposable
{
    private const string TimeoutVariable = "MOTIF_TEST_SQLITE_SHORT_TIMEOUT_FOR";
    private readonly int[] _savedBackoff = BusyRetry.BackoffMilliseconds;

    public void Dispose() => BusyRetry.BackoffMilliseconds = _savedBackoff;

    private static SqliteConnection HoldWriteLock(MotifDatabase database)
    {
        var holder = new SqliteConnection("Data Source=" + database.FullPath);
        holder.Open();
        using var begin = holder.CreateCommand();
        begin.CommandText = "BEGIN IMMEDIATE;";
        begin.ExecuteNonQuery();
        return holder;
    }

    private static void Release(SqliteConnection holder)
    {
        using var end = holder.CreateCommand();
        end.CommandText = "ROLLBACK;";
        end.ExecuteNonQuery();
        holder.Dispose();
    }

    [Fact]
    public void AStageThatMeetsAHeldWriteLockRetriesAndSucceedsOnceItIsReleased()
    {
        using var scratch = pristine.NewScratch();
        var previous = Environment.GetEnvironmentVariable(TimeoutVariable);
        string storePath;
        using (var first = ProjectMotifDatabase.Open(scratch.ProjectId.Path)) storePath = first.FullPath;
        // The short timeout is read at open, so set it first (the store then waits about a second).
        Environment.SetEnvironmentVariable(TimeoutVariable, storePath);
        using var database = ProjectMotifDatabase.Open(scratch.ProjectId.Path);
        var holder = HoldWriteLock(database);
        try
        {
            BusyRetry.BackoffMilliseconds = [10, 10, 10];
            var calls = 0;
            var released = false;
            BusyRetry.Run(() =>
            {
                calls++;
                try { SIL.Motif.Worker.Sources.SourceIndexRefresh.MarkPending(database, [], false); }
                catch (Exception exception) when (SqliteConnections.IsBusyInChain(exception))
                {
                    // Release on the first real BUSY, so the retry is what succeeds, with no wall-clock race.
                    if (!released) { released = true; Release(holder); }
                    throw;
                }
            });
            Assert.True(released, "the first attempt must have met the held lock");
            Assert.Equal(2, calls);
        }
        finally { Environment.SetEnvironmentVariable(TimeoutVariable, previous); }
    }

    [Fact]
    public void AStageThatOutlastsTheBoundSurfacesTheRealBusyError()
    {
        using var scratch = pristine.NewScratch();
        var previous = Environment.GetEnvironmentVariable(TimeoutVariable);
        string storePath;
        using (var first = ProjectMotifDatabase.Open(scratch.ProjectId.Path)) storePath = first.FullPath;
        // The short timeout is read at open, so set it first (the store then waits about a second).
        Environment.SetEnvironmentVariable(TimeoutVariable, storePath);
        using var database = ProjectMotifDatabase.Open(scratch.ProjectId.Path);
        var holder = HoldWriteLock(database);
        try
        {
            BusyRetry.BackoffMilliseconds = [10, 10];
            var thrown = Assert.ThrowsAny<Exception>(() =>
                BusyRetry.Run(() => SIL.Motif.Worker.Sources.SourceIndexRefresh.MarkPending(database, [], false)));
            Assert.True(SqliteConnections.IsBusyInChain(thrown), thrown.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(TimeoutVariable, previous);
            Release(holder);
        }
    }

    [Fact]
    public void BindingSeveralAssessmentsIsOneTransactionSoAFailedTryLeavesNoRows()
    {
        using var scratch = pristine.NewScratch();
        using var database = ProjectMotifDatabase.Open(scratch.ProjectId.Path);
        var repository = new AssessmentRepository(database);
        var selection = SIL.Motif.Host.Corpus.Selection.Create("w", ["w"]);
        foreach (var id in new[] { "assessment/a", "assessment/b" })
            repository.Record(new NewAssessmentRecord(id, null, null, "test", "ParseTime", "{}", "scope",
                new AssessmentSubject.Words(selection, "none", "1"), "{}", null, null, "grammar", null, null, null,
                new AssessmentRecordMaterial.Words([new AssessedWord("w", "analysed", [])], null, null, [])));
        using (var connection = database.OpenConnection())
        using (var trigger = connection.CreateCommand())
        {
            trigger.CommandText = "CREATE TRIGGER refuse_b BEFORE INSERT ON BeforeAssessmentBinding " +
                "WHEN NEW.AssessmentId = 'assessment/b' BEGIN SELECT RAISE(ABORT, 'refused'); END;";
            trigger.ExecuteNonQuery();
        }
        var identity = new BaselineSemanticIdentity("project", "digest", "1");
        var bindings = new BeforeAssessmentBindingRepository(database);

        Assert.ThrowsAny<SqliteException>(() => bindings.BindAll(["assessment/a", "assessment/b"], identity));

        Assert.False(bindings.Contains("assessment/a", identity));
    }
}
