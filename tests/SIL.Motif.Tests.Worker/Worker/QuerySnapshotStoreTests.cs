using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.QueryScale;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker.Worker;

public sealed class QuerySnapshotStoreTests(ITestOutputHelper output)
{
    [Fact]
    public void ReplacesThePreviousTrialAndRollbackPreservesIt()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE query_snapshot(Id INTEGER PRIMARY KEY, TrialId TEXT, GrammarHash TEXT, BundleId TEXT,
                    WritingSystem TEXT, GraphemesJson TEXT, UnresolvedVariants INTEGER, SourceWriteTicks INTEGER,
                    TrialCreatedUtc TEXT, TrialQueueOrder REAL, CoverageJson TEXT);
                CREATE TABLE query_forms(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_stems(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_uses(Ordinal INTEGER PRIMARY KEY, TargetKind TEXT, RowJson TEXT);
                CREATE TABLE query_objects(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                """;
            command.ExecuteNonQuery();
        }
        static QuerySnapshotStore.Stored Make(string id) => new(id, "hash", "bundle", "ws",
            new QueryFactsReader.Snapshot([new QueryForm(id, "form", "base", "alternate", "entry", "stem", "", [], [])],
                ["ng"], 1));
        using (var transaction = connection.BeginTransaction())
        {
            QuerySnapshotStore.Replace(connection, transaction, Make("first"));
            transaction.Commit();
        }
        using (var transaction = connection.BeginTransaction())
        {
            QuerySnapshotStore.Replace(connection, transaction, Make("second"));
            transaction.Rollback();
        }
        using (var transaction = connection.BeginTransaction())
        {
            Assert.Equal("first", QuerySnapshotStore.Read(connection, transaction)!.TrialId);
            QuerySnapshotStore.Replace(connection, transaction, Make("second"));
            transaction.Commit();
        }
        using var read = connection.BeginTransaction();
        var result = QuerySnapshotStore.Read(connection, read)!;
        Assert.Equal("second", result.TrialId);
        Assert.Equal("second", Assert.Single(result.Snapshot.Forms).Id);
        Assert.Equal(["ng"], result.Snapshot.Graphemes);
        var large = Make("large");
        var huge = large.Snapshot.Forms[0] with { Form = new string('x', 36 * 1024 * 1024) };
        Assert.Throws<InvalidDataException>(() => QuerySnapshotStore.Replace(connection, read,
            large with { Snapshot = large.Snapshot with { Forms = [huge] } }));
        Assert.Equal("second", QuerySnapshotStore.Read(connection, read)!.TrialId);
        QuerySnapshotStore.BindBundle(connection, read, "first", "hash", "wrong");
        Assert.Equal("bundle", QuerySnapshotStore.Read(connection, read)!.BundleId);
        QuerySnapshotStore.BindBundle(connection, read, "second", "hash", "retained");
        QuerySnapshotStore.ClearForBundle(connection, read, "wrong");
        Assert.NotNull(QuerySnapshotStore.Read(connection, read));
        QuerySnapshotStore.ClearForBundle(connection, read, "retained");
        Assert.Null(QuerySnapshotStore.Read(connection, read));
    }
    [Fact]
    public void AnOlderEvidenceJobCannotReplaceANewerTrialsRows()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE query_snapshot(Id INTEGER PRIMARY KEY, TrialId TEXT, GrammarHash TEXT, BundleId TEXT,
                    WritingSystem TEXT, GraphemesJson TEXT, UnresolvedVariants INTEGER, SourceWriteTicks INTEGER,
                    TrialCreatedUtc TEXT, TrialQueueOrder REAL, CoverageJson TEXT);
                CREATE TABLE query_forms(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_stems(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_uses(Ordinal INTEGER PRIMARY KEY, TargetKind TEXT, RowJson TEXT);
                CREATE TABLE query_objects(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE Jobs(JobId TEXT, CreatedUtc TEXT, QueueOrder REAL, Kind TEXT);
                INSERT INTO Jobs VALUES ('older','2026-01-01',1,'trial'),('newer','2026-01-02',2,'trial');
                """;
            command.ExecuteNonQuery();
        }
        static QuerySnapshotStore.Stored Rows(string trial) => new(trial, "hash", trial + "-bundle", "ws",
            new QueryFactsReader.Snapshot([new QueryForm(trial, trial, trial, "lexeme", "e", "", "", [], [])], [], 0));
        using var transaction = connection.BeginTransaction();
        QuerySnapshotStore.ReplaceIfNewer(connection, transaction, Rows("newer"));
        QuerySnapshotStore.ReplaceIfNewer(connection, transaction, Rows("older"));
        Assert.Equal("newer", QuerySnapshotStore.Read(connection, transaction)!.TrialId);
        using var delete = connection.CreateCommand();
        delete.Transaction = transaction;
        delete.CommandText = "DELETE FROM Jobs WHERE JobId='newer'";
        delete.ExecuteNonQuery();
        QuerySnapshotStore.ReplaceIfNewer(connection, transaction, Rows("older"));
        Assert.Equal("newer", QuerySnapshotStore.Read(connection, transaction)!.TrialId);
        delete.CommandText = "DELETE FROM Jobs WHERE JobId='older'";
        delete.ExecuteNonQuery();
        QuerySnapshotStore.ReplaceIfNewer(connection, transaction, Rows("older"));
        Assert.Equal("newer", QuerySnapshotStore.Read(connection, transaction)!.TrialId);
        QuerySnapshotStore.Clear(connection, transaction);
        QuerySnapshotStore.ReplaceIfNewer(connection, transaction, Rows("older"));
        Assert.Null(QuerySnapshotStore.Read(connection, transaction));
    }

    [Fact]
    public void OpensReadsAndQueriesThirtyOneThousandFormsCorrectly()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            WriteThirtyOneThousandForms(path);
            var result = OpenReadAndQuery(path);
            Assert.Equal(31000, result.SnapshotFormCount);
            Assert.Equal(31000, result.MatchCount);
            output.WriteLine($"query_snapshot_read_sql_statements={result.ReadStatementCount}");
            Assert.True(result.ReadStatementCount <= 3,
                $"QuerySnapshotStore.Read used {result.ReadStatementCount} SQL statements; the budget is 3.");
        }
        finally { File.Delete(path); }
    }

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void OpensReadsAndQueriesThirtyOneThousandFormsWithinOneSecond()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            WriteThirtyOneThousandForms(path);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = OpenReadAndQuery(path);
            watch.Stop();
            output.WriteLine($"synthetic_store_open_read_query_ms={watch.ElapsedMilliseconds}");
            Assert.Equal(31000, result.SnapshotFormCount);
            Assert.Equal(31000, result.MatchCount);
            Assert.True(watch.ElapsedMilliseconds < 1000, $"Store read and query took {watch.ElapsedMilliseconds} ms");
        }
        finally { File.Delete(path); }
    }

    private static void WriteThirtyOneThousandForms(string path)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE query_snapshot(Id INTEGER PRIMARY KEY, TrialId TEXT, GrammarHash TEXT, BundleId TEXT,
                    WritingSystem TEXT, GraphemesJson TEXT, UnresolvedVariants INTEGER, SourceWriteTicks INTEGER,
                    TrialCreatedUtc TEXT, TrialQueueOrder REAL, CoverageJson TEXT);
                CREATE TABLE query_forms(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_stems(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                CREATE TABLE query_uses(Ordinal INTEGER PRIMARY KEY, TargetKind TEXT, RowJson TEXT);
                CREATE TABLE query_objects(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
                """;
            command.ExecuteNonQuery();
        }
        var forms = Enumerable.Range(0, 31000).Select(i => new QueryForm(i.ToString(), "sarang", "sadang",
            "alternate", i.ToString(), "stem", "", [], [], i)).ToArray();
        using var write = connection.BeginTransaction();
        QuerySnapshotStore.Replace(connection, write, new("trial", "hash", "bundle", "ws",
            new QueryFactsReader.Snapshot(forms, ["ng"], 0)));
        write.Commit();
    }

    private static (int SnapshotFormCount, int MatchCount, int ReadStatementCount) OpenReadAndQuery(string path)
    {
        QuerySnapshotStore.Stored snapshot;
        int readStatementCount;
        using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            connection.Open();
            using var read = connection.BeginTransaction();
            var readStatements = 0;
            SQLitePCL.raw.sqlite3_trace(connection.Handle, (_, sql) =>
            {
                if (!sql.StartsWith("--", StringComparison.Ordinal)) readStatements++;
            }, null);
            snapshot = QuerySnapshotStore.Read(connection, read, [QuerySources.Forms])!;
            readStatementCount = readStatements;
        }
        var result = FormQuery.Run(new QueryRecord("forms", [new("change", "is", "d>r")], ["change.right"]),
            snapshot.Snapshot.Forms, snapshot.Snapshot.Graphemes);
        return (snapshot.Snapshot.Forms.Count, Assert.Single(result.Groups).Count, readStatementCount);
    }

}
