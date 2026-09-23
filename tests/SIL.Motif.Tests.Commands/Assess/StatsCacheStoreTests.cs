using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Assess;

public sealed class StatsCacheStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assessment-runs-" + Guid.NewGuid().ToString("N"));
    private readonly StatsCacheStore _store;

    public StatsCacheStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new StatsCacheStore(WorkspaceOwnership.Bootstrap(_root));
    }

    [Fact]
    public void InvocationIdentityDeterminesTheDirectoryWithoutCreatingIt()
    {
        var first = _store.DirectoryFor("invocation-one");
        Assert.Equal(first, _store.DirectoryFor("invocation-one"));
        Assert.NotEqual(first, _store.DirectoryFor("invocation-two"));
        Assert.Equal(Path.Combine(_root, "assessment-runs", "invocation-one"), first);
        Assert.True(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.False(Directory.Exists(first));
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("drive:stream")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("wild*card")]
    [InlineData("CON")]
    public void UnsafeInvocationIdsAreRefusedBeforeCreatingAnyRunParent(string invocationId)
    {
        Assert.Throws<ArgumentException>(() => _store.DirectoryFor(invocationId));
        Assert.False(Directory.Exists(Path.Combine(_root, "assessment-runs")));
    }

    [RequiresSymbolicLinkFact]
    public void AReparsePointAtTheRunParentIsRefused()
    {
        var outside = Path.Combine(_root, "outside-target");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(_root, "assessment-runs");
        Directory.CreateSymbolicLink(link, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _store.DirectoryFor("invocation-one"));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally { Directory.Delete(link); }
    }

    [RequiresSymbolicLinkFact]
    public void AReparsePointAtTheRunDirectoryIsRefused()
    {
        var outside = Path.Combine(_root, "outside-target");
        Directory.CreateDirectory(outside);
        var path = _store.DirectoryFor("invocation-one");
        Directory.CreateSymbolicLink(path, outside);
        try
        {
            Assert.Throws<InvalidOperationException>(() => _store.DirectoryFor("invocation-one"));
        }
        finally { Directory.Delete(path); }
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }
}
