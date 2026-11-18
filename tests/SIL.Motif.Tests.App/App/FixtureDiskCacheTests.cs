using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class FixtureDiskCacheTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-fixture-cache-").FullName;

    [Fact]
    public void PublicationKeepsTheWinnerAndDiscardsTheLosingBuild()
    {
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var final = Path.Combine(_root, "published");
        var winner = Directory.CreateDirectory(Path.Combine(_root, "winner")).FullName;
        File.WriteAllText(Path.Combine(winner, "payload"), "winner");
        FixtureDiskCache.Publish(winner, final, "winner");

        var built = FixtureDiskCache.Publish(staging, final, "loser");

        Assert.True(built.Shared);
        Assert.Equal(final, built.Root);
        Assert.Equal("winner", built.Metadata);
        Assert.Equal("winner", File.ReadAllText(Path.Combine(built.Root, "payload")));
        Assert.False(Directory.Exists(staging));
    }

    [WindowsFileLockFact]
    public void ASharingDeniedRenameLeavesACompletePrivateBuild()
    {
        var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
        var final = Path.Combine(_root, "published");
        var path = Path.Combine(staging, "payload");
        File.WriteAllText(path, "private");
        using var held = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        var built = FixtureDiskCache.Publish(staging, final, "private");

        Assert.False(built.Shared);
        Assert.Equal(staging, built.Root);
        Assert.Equal("private", built.Metadata);
        Assert.Equal("private", File.ReadAllText(Path.Combine(built.Root, "payload")));
        Assert.False(Directory.Exists(final));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
