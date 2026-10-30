using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>The retention sweep must stay cheap and stoppable however much word evidence a store holds.</summary>
public sealed class AssessmentSweepIndexTests : IDisposable
{
    private const int Runs = 25;
    private const int WordsPerRun = 40;
    private const int Keep = 20;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-sweep-index-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;

    public AssessmentSweepIndexTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "sweep.fwdata"), "sweep");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "sweep.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var repository = new AssessmentRepository(_database);
        var start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (var run = 0; run < Runs; run++)
            repository.Record(NewRun($"run-{run:D2}", run, start.AddMinutes(run)));
    }

    [Fact]
    public void SweepRemovesOnlyTheOldestStandaloneRunsAndTheirUnsharedPayloads()
    {
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        using var connection = _database.OpenConnection();
        var progress = new List<string>();
        using (var transaction = connection.BeginTransaction())
        {
            var removed = AssessmentRetention.SweepStandalone(connection, transaction, lease, Keep, [],
                out _, progress: progress.Add);
            transaction.Commit();
            Assert.Equal(Runs - Keep, removed);
        }

        var remaining = Strings(connection, "SELECT AssessmentId FROM Assessments ORDER BY AssessmentId;");
        Assert.Equal(Enumerable.Range(Runs - Keep, Keep).Select(run => $"run-{run:D2}"), remaining);
        Assert.Equal(Runs - Keep, progress.Count);
        Assert.All(Enumerable.Range(0, Runs - Keep), run => Assert.Contains($"run-{run:D2}", progress[run]));
        Assert.Equal(Keep * WordsPerRun, Scalar(connection, "SELECT COUNT(*) FROM AssessedWords;"));
        Assert.Equal(Scalar(connection, """
            SELECT COUNT(*) FROM (SELECT MorphologyJson FROM AssessedWords WHERE MorphologyJson IS NOT NULL
                UNION SELECT CorrectnessJson FROM AssessedWords WHERE CorrectnessJson IS NOT NULL);
            """), Scalar(connection, "SELECT COUNT(*) FROM AssessmentWordPayloads;"));
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        using var violations = check.ExecuteReader();
        Assert.False(violations.Read());
    }

    [Theory]
    [InlineData("MorphologyJson")]
    [InlineData("CorrectnessJson")]
    [InlineData("ReadingGradesJson")]
    [InlineData("AnalysisComparisonJson")]
    [InlineData("MissedApprovedJson")]
    public void EveryPayloadForeignKeyColumnIsLookedUpThroughAnIndex(string column)
    {
        using var connection = _database.OpenConnection();
        using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = "SELECT COUNT(*) FROM pragma_foreign_key_list('AssessedWords') " +
            "WHERE \"table\" = 'AssessmentWordPayloads' AND \"from\" = $column;";
        foreignKeys.Parameters.AddWithValue("$column", column);
        Assert.Equal(1L, (long)foreignKeys.ExecuteScalar()!);

        // The foreign-key check of a payload delete finds its referencing words with exactly this lookup.
        using var plan = connection.CreateCommand();
        plan.CommandText = $"EXPLAIN QUERY PLAN SELECT 1 FROM AssessedWords WHERE {column} = x'00';";
        using var reader = plan.ExecuteReader();
        var detail = new List<string>();
        while (reader.Read()) detail.Add(reader.GetString(3));
        Assert.Contains(detail, line => line.Contains("USING") && line.Contains("INDEX IX_AssessedWords_" + column));
        Assert.DoesNotContain(detail, line => line.StartsWith("SCAN", StringComparison.Ordinal));
    }

    [Fact]
    public void ACancelledSweepStopsBetweenAssessmentsAndLeavesWholeRuns()
    {
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        using var connection = _database.OpenConnection();
        using var source = new CancellationTokenSource();
        var progress = new List<string>();
        int removed;
        using (var transaction = connection.BeginTransaction())
        {
            removed = AssessmentRetention.SweepStandalone(connection, transaction, lease, Keep, [], out _,
                source.Token, line => { progress.Add(line); source.Cancel(); });
            transaction.Commit();
        }

        Assert.Equal(1, removed);
        Assert.Single(progress);
        Assert.Equal(Runs - 1, Scalar(connection, "SELECT COUNT(*) FROM Assessments;"));
        // Every surviving Assessment still has all its words, and every word's payloads still exist.
        Assert.Equal((Runs - 1) * WordsPerRun, Scalar(connection, "SELECT COUNT(*) FROM AssessedWords;"));
        using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA foreign_key_check;";
        using var violations = check.ExecuteReader();
        Assert.False(violations.Read());
    }

    [Fact]
    public void ASweepCancelledBeforeItStartsRemovesNothing()
    {
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var removed = AssessmentRetention.SweepStandalone(connection, transaction, lease, Keep, [], out var invocations,
            new CancellationToken(canceled: true));
        transaction.Commit();
        Assert.Equal(0, removed);
        Assert.Empty(invocations);
        Assert.Equal(Runs, Scalar(connection, "SELECT COUNT(*) FROM Assessments;"));
    }

    private static NewAssessmentRecord NewRun(string id, int run, DateTimeOffset saved)
    {
        var words = Enumerable.Range(0, WordsPerRun)
            .Select(index => SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word($"w{run:D2}-{index:D3}", true)).ToArray();
        return new NewAssessmentRecord(
            AssessmentId: id, ProposalId: null, ProposalIntentDigest: null, Assessor: "pangloss", Kind: "ParseTime",
            ScopeJson: "{\"words\":\"all\"}", ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("selection-1", ["bo", "za"]), "whitespace", "1"),
            BaselineToken: "{\"baseline\":true}", OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
            DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words(words, null, null, []),
            SavedUtc: saved.ToString("O"));
    }

    private static long Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static List<string> Strings(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
