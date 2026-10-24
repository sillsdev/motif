using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class StaleTestDirectoriesTests
{
    [Fact]
    public void TemporaryRootSweepIncludesStoredSelectionDatabasesAndKeepsLiveAndNeighboringProjects()
    {
        var fixtureRootName = StoredSelectionFixture.TemporaryRootFolder;
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "motif-stale-fixture-root-" + Guid.NewGuid().ToString("N"));
        var fixtureRoot = Path.Combine(temporaryRoot, fixtureRootName);
        var stale = Directory.CreateDirectory(Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"))).FullName;
        var live = Directory.CreateDirectory(Path.Combine(fixtureRoot, Guid.NewGuid().ToString("N"))).FullName;
        var neighboringProject = Directory.CreateDirectory(Path.Combine(temporaryRoot, "user-project")).FullName;
        var wal = Path.Combine(stale, "project.motif.db-wal");
        var project = Path.Combine(neighboringProject, "project.fwdata");
        File.WriteAllBytes(wal, new byte[8 * 1024 * 1024]);
        File.WriteAllText(project, "preserved project data");
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(stale, now - StaleTestDirectories.StaleAfter - TimeSpan.FromMinutes(1));

        try
        {
            Assert.Contains(fixtureRootName, StaleTestDirectories.TemporaryRoots);
            Assert.Equal(8 * 1024 * 1024, new FileInfo(wal).Length);
            Assert.Equal(1, StaleTestDirectories.Sweep(fixtureRoot, now - StaleTestDirectories.StaleAfter));

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(live));
            Assert.True(File.Exists(project));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void SweepDeletesOnlyDirectoriesOlderThanTheCutoff()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-stale-sweep-" + Guid.NewGuid().ToString("N"));
        var stale = Directory.CreateDirectory(Path.Combine(root, "stale")).FullName;
        var live = Directory.CreateDirectory(Path.Combine(root, "live")).FullName;
        File.WriteAllText(Path.Combine(stale, "left.txt"), "left behind");
        var now = DateTime.UtcNow;
        Directory.SetLastWriteTimeUtc(stale, now - TimeSpan.FromDays(2));
        try
        {
            Assert.Equal(1, StaleTestDirectories.Sweep(root, now - StaleTestDirectories.StaleAfter));

            Assert.False(Directory.Exists(stale));
            Assert.True(Directory.Exists(live));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SweepingARootThatDoesNotExistDeletesNothing() =>
        Assert.Equal(0, StaleTestDirectories.Sweep(
            Path.Combine(Path.GetTempPath(), "motif-absent-" + Guid.NewGuid().ToString("N")), DateTime.UtcNow));
}
