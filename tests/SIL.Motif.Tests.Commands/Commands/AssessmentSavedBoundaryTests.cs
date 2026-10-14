using SIL.Motif.Commands;
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
public sealed class AssessmentSavedBoundaryTests(PristineProjectFixture pristine)
{
    [Fact]
    public void ABusyStoreAfterAnAssessmentIsSavedStillReportsTheSavedAssessmentAndKeepsItsArtifacts()
    {
        using var cache = pristine.NewScratch();
        SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var root = Directory.CreateTempSubdirectory("motif-saved-boundary-").FullName;
        var run = Path.Combine(root, "invocation");
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var logPath = ProjectDiagnosticLog.PathFor(projectPath);
        const string variable = "MOTIF_TEST_BUSY_PARSER_REFUSAL_CLEAR_FOR";
        var previous = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, projectPath);
        try
        {
            var result = AssessCommand.Run(
                new AssessRequest(projectPath, new SelectionRequest(true, [], [], false, null)),
                Path.Combine(root, "managed"), new SavedAssessor(run), new FakeInvoker(), null, CancellationToken.None);
            Assert.True(result.Succeeded);
            Assert.True(Directory.Exists(run));
            Assert.True(File.Exists(Path.Combine(run, "source.fwdata")));
            Assert.True(File.Exists(Path.Combine(run, "stats.sqlite")));
            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            connection.Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM Assessments;";
            Assert.Equal(2L, count.ExecuteScalar());
            count.CommandText = "SELECT COUNT(*) FROM AssessmentInvocations;";
            Assert.Equal(1L, count.ExecuteScalar());
            Assert.Contains("database is locked", File.ReadAllText(logPath));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, previous);
            if (File.Exists(logPath)) File.Delete(logPath);
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class SavedAssessor(string directory) : IAssessor
    {
        public string Name => "saved-assessor";
        public IReadOnlyList<AssessmentKind> SupportedKinds => [AssessmentKind.ParseTime, AssessmentKind.ObjectTiming];
        public Task<IReadOnlyList<ProducedAssessment>> ProduceAsync(
            AssessmentScope scope, string exportedCandidate, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "source.fwdata");
            var statistics = Path.Combine(directory, "stats.sqlite");
            File.Copy(Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories).Single(), source);
            WriteStatsCache(statistics, scope.Words);
            var projectPath = Directory.GetFiles(exportedCandidate, "*.fwdata", SearchOption.AllDirectories).Single();
            var words = scope.Words.Select((word, index) => new WordAnalysis(
                index, word, 1, WordOutcome.NoAnalysis, "none")).ToArray();
            var evidence = new BatchInvocationEvidence("invocation", source, BatchInvocationEvidence.DigestFile(source),
                "sha256:executable", "words", "sha256:words", "tsv", "sha256:tsv", "stderr", "sha256:stderr",
                1000, 200000, 1, true);
            var lease = new AssessmentArtifactLease(directory);
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
                CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
                INSERT INTO cache_identity VALUES (1, 8);
                CREATE TABLE word (
                    word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                    attempts INTEGER NOT NULL, passes INTEGER NOT NULL, capped INTEGER NOT NULL,
                    timed_out INTEGER NOT NULL, invalid_shape INTEGER NOT NULL);
                CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL,
                    label TEXT NOT NULL, identity_quality TEXT NOT NULL);
                CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL,
                    direction TEXT NOT NULL, attempts INTEGER NOT NULL, self_time_ns INTEGER NOT NULL);
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
