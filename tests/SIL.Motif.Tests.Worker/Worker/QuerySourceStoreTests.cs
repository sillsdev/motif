using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Query;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.QueryScale;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker.Worker;

public sealed class QuerySourceStoreTests(ITestOutputHelper output)
{
    private const string Schema = """
        CREATE TABLE query_snapshot(Id INTEGER PRIMARY KEY, TrialId TEXT, GrammarHash TEXT, BundleId TEXT,
            WritingSystem TEXT, GraphemesJson TEXT, UnresolvedVariants INTEGER, SourceWriteTicks INTEGER,
            TrialCreatedUtc TEXT, TrialQueueOrder REAL);
        CREATE TABLE query_forms(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
        CREATE TABLE query_stems(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
        CREATE TABLE query_uses(Ordinal INTEGER PRIMARY KEY, TargetKind TEXT, RowJson TEXT);
        CREATE TABLE query_objects(Ordinal INTEGER PRIMARY KEY, RowJson TEXT);
        """;

    private static QueryFactsReader.Snapshot Rows(int stems, int uses, int objects) => new(
        [new QueryForm("f", "form", "base", "lexeme", "e", "", "", [], [])], ["ng"], 0,
        [.. Enumerable.Range(0, stems).Select(i => new QueryRow("s" + i,
            [["sarang" + i % 50], ["e" + i], ["V"], i % 7 == 0 ? ["CLF"] : [], ["go"], ["1"], [(i % 3).ToString()]]))],
        [.. Enumerable.Range(0, uses).Select(i => new QueryRow("u" + i,
            [[i % 3 == 0 ? "environment" : "category"], ["env" + i % 300], ["allomorph"], ["a" + i], ["phone_env"], [i % 4 == 0 ? "applied" : "ignored"]]))],
        [.. Enumerable.Range(0, objects).Select(i => new QueryRow("o" + i,
            [["environment"], ["label" + i], [(i % 5).ToString()], [(i % 2).ToString()], [], [], ["k" + i % 40 + "‖r"]]))]);

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();
        return connection;
    }

    private static void Write(string path, QueryFactsReader.Snapshot snapshot)
    {
        using var connection = Open(path);
        using var write = connection.BeginTransaction();
        QuerySnapshotStore.Replace(connection, write, new("trial", "hash", "bundle", "ws", snapshot));
        write.Commit();
    }

    private static (QuerySnapshotStore.Stored Stored, int Statements) Read(string path, string[]? sources, string[]? useKinds = null)
    {
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var read = connection.BeginTransaction();
        var statements = 0;
        SQLitePCL.raw.sqlite3_trace(connection.Handle, (_, sql) => { if (!sql.StartsWith("--", StringComparison.Ordinal)) statements++; }, null);
        return (QuerySnapshotStore.Read(connection, read, sources, useKinds)!, statements);
    }

    [Fact]
    public void StemsUsesAndObjectsRoundTripAndAReadDecodesOnlyTheSourcesAsked()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            Write(path, Rows(10, 20, 5));
            var all = Read(path, null).Stored.Snapshot;
            Assert.Equal((1, 10, 20, 5), (all.Forms.Count, all.Stems!.Count, all.Uses!.Count, all.Objects!.Count));
            var onlyUses = Read(path, [QuerySources.Uses]).Stored.Snapshot;
            Assert.Equal((0, 0, 20, 0), (onlyUses.Forms.Count, onlyUses.Stems!.Count, onlyUses.Uses!.Count, onlyUses.Objects!.Count));
            Assert.Equal(["ng"], onlyUses.Graphemes);
            Assert.Equal(["sarang3"], all.Stems[3].Values[0]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadingASourceCostsAtMostTwoStatementsHoweverManyRowsItHolds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            Write(path, Rows(5000, 5000, 2000));
            foreach (var source in new[] { QuerySources.Stems, QuerySources.Uses, QuerySources.Objects })
            {
                var (stored, statements) = Read(path, [source]);
                output.WriteLine($"{source} read_sql_statements={statements}");
                Assert.True(statements <= 2, $"Reading {source} used {statements} SQL statements; the budget is 2.");
                Assert.Equal(source == QuerySources.Objects ? 2000 : 5000, source switch
                {
                    QuerySources.Stems => stored.Snapshot.Stems!.Count,
                    QuerySources.Uses => stored.Snapshot.Uses!.Count,
                    _ => stored.Snapshot.Objects!.Count,
                });
            }
            Assert.True(Read(path, null).Statements <= 5);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ATargetKindLimitsTheUsesRowsDecodedInOneStatement()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            Write(path, Rows(1, 3000, 1));
            var (stored, statements) = Read(path, [QuerySources.Uses], ["environment"]);
            Assert.Equal(1000, stored.Snapshot.Uses!.Count);
            Assert.All(stored.Snapshot.Uses, row => Assert.Equal("environment", row.Values[0][0]));
            Assert.True(statements <= 2);
            Assert.Equal(3000, Read(path, [QuerySources.Uses]).Stored.Snapshot.Uses!.Count);
            Assert.Equal(3000, Read(path, [QuerySources.Uses], ["category", "environment"]).Stored.Snapshot.Uses!.Count);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ARowOverTheCapIsRefusedBeforeItIsWritten()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            var huge = new string('x', 1024 * 1024);
            var snapshot = new QueryFactsReader.Snapshot([], [], 0, [.. Enumerable.Range(0, 101).Select(i => new QueryRow("s" + i, [[huge]]))]);
            using var connection = Open(path);
            using var write = connection.BeginTransaction();
            Assert.Throws<InvalidDataException>(() => QuerySnapshotStore.Replace(connection, write, new("t", "h", "b", "ws", snapshot)));
        }
        finally { File.Delete(path); }
    }

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void EachNewSourceReadsAndQueriesWithinFourHundredMilliseconds()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            Write(path, Rows(34000, 100000, 5000));
            var slow = new List<string>();
            foreach (var (source, query) in new[]
                     {
                         (QuerySources.Stems, new QueryRecord("stems", [new("final:2", "~", "^[aiu]d$")], ["shape"])),
                         (QuerySources.Stems, new QueryRecord("stems", [new("category", "is", "none")], [], Ids: true)),
                         (QuerySources.Uses, new QueryRecord("uses", [new("target-kind", "is", "environment")], ["target"])),
                         (QuerySources.Objects, new QueryRecord("objects", [new("class", "is", "environment")], ["key"], MinCount: 2)),
                     })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var stored = Read(path, [source], query.Where.FirstOrDefault(c => c.Col == "target-kind")?.Value.Split(',')).Stored.Snapshot;
                var readMs = watch.ElapsedMilliseconds;
                var result = FormQuery.RunRows(query, source switch
                {
                    QuerySources.Stems => stored.Stems!, QuerySources.Uses => stored.Uses!, _ => stored.Objects!,
                }, stored.Graphemes);
                watch.Stop();
                output.WriteLine($"{source} {string.Join(",", query.Group)} read_query_ms={watch.ElapsedMilliseconds} groups={result.Groups.Count}");
                if (watch.ElapsedMilliseconds >= 400) slow.Add($"{source} {string.Join(",", query.Group)} took {watch.ElapsedMilliseconds} ms (read {readMs})");
            }
            Assert.True(slow.Count == 0, string.Join("; ", slow));
        }
        finally { File.Delete(path); }
    }
}
