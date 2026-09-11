using Microsoft.Data.Sqlite;
using System.Runtime.Versioning;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[SupportedOSPlatform("windows")]
[Collection(LcmCacheTestCollection.Name)]
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
            mode == "cancelled-after-producer" ? () => cancellation.Cancel() : null);
        var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var invoker = new FakeInvoker { Respond = _ =>
        {
            if (mode == "summary-refused") return new PanGlossOutcome.Unavailable("summary refused");
            if (mode == "cancelled-after-summary") cancellation.Cancel();
            if (mode == "database-refused")
            {
                using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TRIGGER reject_statistics BEFORE INSERT ON Assessments " +
                    "WHEN NEW.Kind = 'ObjectTiming' BEGIN SELECT RAISE(ABORT, 'record refused'); END;";
                command.ExecuteNonQuery();
            }
            return new PanGlossOutcome.Completed("statistics", string.Empty, TimeSpan.Zero);
        }};
        try
        {
            var failure = Record.Exception(() =>
            {
                var result = AssessCommand.Run(new AssessRequest(projectPath, new SelectionRequest(true, [], [], false, null)),
                    Path.Combine(root, "managed"), assessor, invoker, null, cancellation.Token);
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

    private sealed class ArtifactAssessor(string directory, Action? afterProduction) : IAssessor
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
            File.WriteAllText(statistics, "stats");
            var evidence = new BatchInvocationEvidence("invocation", source, BatchInvocationEvidence.DigestFile(source),
                "sha256:executable", "words", "sha256:words", "tsv", "sha256:tsv", "stderr", "sha256:stderr",
                1000, 200000, 1, true);
            var lease = new AssessmentArtifactLease(directory);
            afterProduction?.Invoke();
            return Task.FromResult<IReadOnlyList<ProducedAssessment>>([
                new(AssessmentKind.ParseTime, evidence.SourceBytesSha256, null, null, null, null, null,
                    new AssessmentRaw.WordMeasurements([])) { Invocation = evidence, ArtifactLease = lease },
                new(AssessmentKind.ObjectTiming, evidence.SourceBytesSha256, null, null, null, null, null,
                    new AssessmentRaw.FileCache(statistics, BatchInvocationEvidence.DigestFile(statistics)))
                    { Invocation = evidence, ArtifactLease = lease }
            ]);
        }
    }
}
