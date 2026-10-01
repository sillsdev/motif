using Microsoft.Data.Sqlite;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class AssessmentArtifactLifetimeTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData("completed")]
    [InlineData("summary-refused")]
    [InlineData("cancelled-after-producer")]
    [InlineData("cancelled-after-summary")]
    [InlineData("database-refused")]
    public void OnlyAnAtomicCommitRetainsTheProducedArtifacts(string mode)
    {
        using var cache = pristine.NewScratch();
        SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var root = Directory.CreateTempSubdirectory("motif-artifact-lifetime-").FullName;
        var run = Path.Combine(root, "invocation");
        using var cancellation = new CancellationTokenSource();
        var assessor = new ArtifactAssessor(run,
            mode == "cancelled-after-producer" ? () => cancellation.Cancel() : null,
            mode == "summary-refused");
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var invoker = new FakeInvoker();
        Action<AssessmentProgress>? progress = mode is "cancelled-after-summary" or "database-refused"
            ? update =>
            {
                if (update.Stage != AssessmentStage.ReadingStatistics) return;
                if (mode == "cancelled-after-summary") cancellation.Cancel();
                else
                {
                    using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
                    connection.Open();
                    using var command = connection.CreateCommand();
                    command.CommandText = "CREATE TRIGGER reject_statistics BEFORE INSERT ON Assessments " +
                        "BEGIN SELECT RAISE(ABORT, 'record refused'); END;";
                    command.ExecuteNonQuery();
                }
            }
            : null;
        try
        {
            var failure = Record.Exception(() =>
            {
                var result = AssessCommand.Run(new AssessRequest(projectPath, new SelectionRequest(true, [], [], false, null)),
                    Path.Combine(root, "managed"), assessor, invoker, progress, cancellation.Token);
                Assert.Equal(mode == "completed", result.Succeeded);
            });
            if (mode == "database-refused") Assert.IsType<SqliteException>(failure);
            else Assert.Null(failure);
            Assert.Equal(mode == "completed", Directory.Exists(run));
            if (mode == "cancelled-after-producer") Assert.Empty(invoker.Requests);
            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            connection.Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Assessments;";
            Assert.Equal(mode == "completed" ? 2L : 0L, count.ExecuteScalar());
            count.CommandText = "SELECT COUNT(*) FROM AssessmentInvocations;";
            Assert.Equal(mode == "completed" ? 1L : 0L, count.ExecuteScalar());
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RetainedLeaseDisposalIsIdempotentAndDoesNotDeleteOtherRuns()
    {
        var root = Directory.CreateTempSubdirectory("motif-artifact-lease-").FullName;
        try
        {
            var retained = Directory.CreateDirectory(Path.Combine(root, "retained")).FullName;
            var abandoned = Directory.CreateDirectory(Path.Combine(root, "abandoned")).FullName;
            var lease = new AssessmentArtifactLease(retained);
            lease.Retain();
            lease.Dispose();
            lease.Dispose();
            new AssessmentArtifactLease(abandoned).Dispose();
            Assert.True(Directory.Exists(retained));
            Assert.False(Directory.Exists(abandoned));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class ArtifactAssessor(string directory, Action? afterProduction, bool omitStatisticsRows) : IAssessor
    {
        public string Name => "artifact-assessor";
        public IReadOnlyList<AssessmentKind> SupportedKinds => [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];
        public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
            AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "source.fwdata");
            var statistics = Path.Combine(directory, "stats.sqlite");
            File.Copy(Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories).Single(), source);
            WriteStatsCache(statistics, omitStatisticsRows ? Array.Empty<string>() : scope.Words);
            var projectPath = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories).Single();
            var words = scope.Words.Select((word, index) => new WordAnalysis(
                index, word, 1, WordOutcome.NoAnalysis, "none")).ToArray();
            var evidence = new BatchInvocationEvidence("invocation", source, BatchInvocationEvidence.DigestFile(source),
                "sha256:executable", "words", "sha256:words", "tsv", "sha256:tsv", "stderr", "sha256:stderr",
                1000, 200000, 1, true);
            var lease = new AssessmentArtifactLease(directory);
            afterProduction?.Invoke();
            return Task.FromResult<IReadOnlyList<ProducedAssessment>>([
                new(AssessmentKind.ParseTime, evidence.SourceBytesSha256, null, null, null, null, null,
                    new AssessmentRaw.Batch(new BatchAnalysis(words, 1000, projectPath, [])
                    { PerWordStepLimit = scope.PerWordStepLimit })) { Invocation = evidence, ArtifactLease = lease },
                new(AssessmentKind.ObjectTiming, evidence.SourceBytesSha256, null, null, null, null, null,
                    new AssessmentRaw.FileCache(statistics, BatchInvocationEvidence.DigestFile(statistics)))
                    { Invocation = evidence, ArtifactLease = lease }
            ]);
        }

        private static void WriteStatsCache(string path, IReadOnlyList<string> words)
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
            }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE word (
                    word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                    attempts INTEGER NOT NULL, passes INTEGER NOT NULL, capped INTEGER NOT NULL,
                    timed_out INTEGER NOT NULL, invalid_shape INTEGER NOT NULL);
                CREATE TABLE object (object_id INTEGER PRIMARY KEY, kind TEXT NOT NULL, label TEXT NOT NULL);
                CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                    attempts INTEGER NOT NULL, self_time_ns INTEGER NOT NULL);
                """;
            command.ExecuteNonQuery();
            foreach (var word in words)
            {
                using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO word (form, elapsed_ns, attempts, passes, capped, timed_out, invalid_shape)
                    VALUES ($form, 0, 1, 0, 0, 0, 0);
                    """;
                insert.Parameters.AddWithValue("$form", word);
                insert.ExecuteNonQuery();
            }
        }
    }
}
