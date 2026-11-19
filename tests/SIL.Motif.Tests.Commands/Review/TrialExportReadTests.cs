using SIL.Motif.Commands.Review;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands.Review;

public sealed class TrialExportReadTests
{
    [Fact]
    public void ReplacementStoreCannotReadAnArtifactAtTheOldPath()
    {
        using var fixture = new SIL.Motif.Tests.TestFixtures.ReviewAcceptanceFixture();
        var receipt = fixture.CreateInterruptedTrialExport();
        var project = new ProjectLocator(fixture.Project, Path.GetFileNameWithoutExtension(fixture.Project));
        var store = ProjectDatabaseCatalog.DatabasePathFor(project);
        File.Move(store, store + ".original");
        using var replacement = Open(project);
        Assert.True(File.Exists(receipt.Path));

        var result = TrialExportReads.Read(new(fixture.Project, receipt.JobId));

        Assert.Equal("trial.export-unavailable", result.Refusal?.Code);
    }

    [Fact]
    public void TrialExportReportsSizeAboveTheFormerLimit()
    {
        InStore(string.Empty, (database, project, id) =>
        {
            const int words = 513;
            var sample = Word(new string('x', 1024 * 1024));
            TrialExportArtifact.Publish(database, id, Counts(words), Provenance(), [],
                Enumerable.Repeat(sample, words));

            var result = TrialExportReads.Read(new(project.FullFwDataPath, id));
            Assert.True(result.Succeeded, result.Refusal?.Message);
            Assert.True(result.Value!.Bytes > 512L * 1024 * 1024);
            Assert.Equal(result.Value.Bytes, new FileInfo(result.Value.Path).Length);
        });
    }

    [Fact]
    public void ReceiptRemainsWithinTwoKilobytesWithAnEscapedProjectPath()
    {
        var segment = new string('&', 83);
        InStore(Path.Combine(segment, segment, segment, segment), (database, project, id) =>
        {
            TrialExportArtifact.Publish(database, id, Counts(0), Provenance(), [], []);
            var result = TrialExportReads.Read(new(project.FullFwDataPath, id));

            Assert.True(result.Succeeded, result.Refusal?.Message);
            var receipt = result.Value!;
            Assert.InRange(ProjectionJson.ToUtf8Bytes(receipt).Length, 1, 2000);
            Assert.True(receipt.PathIsStoreSuffix);
            Assert.True(File.Exists(database.FullPath + receipt.Path));
        });
    }

    private static MotifDatabase Open(ProjectLocator project) =>
        MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, new(1, 0));

    private static void InStore(string subdirectory, Action<MotifDatabase, ProjectLocator, string> test)
    {
        var root = Directory.CreateTempSubdirectory("motif-trial-export-read-").FullName;
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, subdirectory)).FullName;
            var path = Path.Combine(folder, "project.fwdata");
            File.WriteAllText(path, "");
            var project = new ProjectLocator(path, "project");
            using var database = Open(project);
            test(database, project, "job/00000000-0000-0000-0000-000000000099");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static TrialExportProvenance Provenance()
    {
        var digest = "sha256:" + new string('a', 64);
        return new(null, "proposal/test", "test-revision", new("motif-intent-v1", digest),
            new("project", digest, "test", "2026-01-01T00:00:00Z", digest), null, null, new(null, null), new(null, null), null);
    }

    private static TrialExportDenominators Counts(int words) => new(words, words, 0, new(0, 0), new(0, 0));

    private static TrialExportWord Word(string form) =>
        new("case", null, form, [], [], TrialExportStatus.Skipped, "not-measured", []);
}
