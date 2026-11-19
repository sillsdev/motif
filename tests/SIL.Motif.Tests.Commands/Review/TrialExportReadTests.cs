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

    [Theory]
    [InlineData(259, false)] // the longest path Windows can open, escaped at worst: still within the bound as-is
    [InlineData(1200, true)] // an extended-length path: elided to the store suffix
    public void ReceiptRemainsWithinTwoKilobytesWithAWindowsStyleEscapedPath(int pathLength, bool elided)
    {
        InStore("worst-case", (database, project, id) =>
        {
            TrialExportArtifact.Publish(database, id, Counts(0), Provenance(), [], []);
            var receipt = TrialExportReads.Read(new(project.FullFwDataPath, id)).Value!;
            Assert.False(receipt.PathIsStoreSuffix);

            // Backslash separators and '&' (six bytes once JSON-escaped) under a long Windows temp prefix.
            var prefix = @"C:\Users\a-very-long-user-name-for-a-build-agent\AppData\Local\Temp\motif-trial-export-read-0123456789\";
            var path = prefix + string.Concat(Enumerable.Repeat(new string('&', 30) + "\\", pathLength / 31));
            path = path[..Math.Min(path.Length, pathLength)];
            var suffix = Path.Combine(MotifDatabase.TrialExportDirectory(""), new string('a', 64) + "." + new string('b', 64) + ".json");

            var bounded = TrialExportArtifact.WithinBound(receipt with { Path = path }, suffix);

            Assert.InRange(ProjectionJson.ToUtf8Bytes(bounded).Length, 1, 2000);
            Assert.Equal(elided, bounded.PathIsStoreSuffix);
            Assert.Equal(elided ? suffix : path, bounded.Path);
        });
    }

    [Fact]
    public void ReceiptRemainsWithinTwoKilobytesWithAnEscapedProjectPath()
    {
        // Windows cannot open paths past 259 characters, so only other OSes go that long.
        var segment = new string('&', OperatingSystem.IsWindows() ? 20 : 83);
        InStore(Path.Combine(segment, segment, segment, segment), (database, project, id) =>
        {
            TrialExportArtifact.Publish(database, id, Counts(0), Provenance(), [], []);
            var result = TrialExportReads.Read(new(project.FullFwDataPath, id));

            Assert.True(result.Succeeded, result.Refusal?.Message);
            var receipt = result.Value!;
            Assert.InRange(ProjectionJson.ToUtf8Bytes(receipt).Length, 1, 2000);
            Assert.Equal(!OperatingSystem.IsWindows(), receipt.PathIsStoreSuffix);
            Assert.True(File.Exists(database.FullPath + receipt.Path) || File.Exists(receipt.Path));
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
