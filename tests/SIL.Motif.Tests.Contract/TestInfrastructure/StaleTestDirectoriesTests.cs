using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class StaleTestDirectoriesTests
{
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
