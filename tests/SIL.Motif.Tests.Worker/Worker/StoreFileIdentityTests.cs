using SIL.Motif.Worker.Projects;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class StoreFileIdentityTests
{
    [Fact]
    public void AFileRecreatedWithTheInodeTheDeletedOneFreedIsReplacedWhenTheFileSystemReportsBirthTimes()
    {
        var opened = new StoreFileIdentity(7, 42, 100, BirthTicks: 5_000);
        var recreated = new StoreFileIdentity(7, 42, 100, BirthTicks: 9_000);

        Assert.True(opened.IsReplacedBy(recreated));
    }

    [Fact]
    public void TheSameFileIsNotReplacedWhateverItsContentsDo()
    {
        var opened = new StoreFileIdentity(7, 42, 100, BirthTicks: 5_000);

        Assert.False(opened.IsReplacedBy(new StoreFileIdentity(7, 42, 999, BirthTicks: 5_000)));
    }

    [Fact]
    public void ABirthTimeTheFileSystemDoesNotReportNeverCountsAsAReplacement()
    {
        var opened = new StoreFileIdentity(7, 42, 100, BirthTicks: 0);

        Assert.False(opened.IsReplacedBy(new StoreFileIdentity(7, 42, 100, BirthTicks: 9_000)));
        Assert.False(new StoreFileIdentity(7, 42, 100, BirthTicks: 9_000).IsReplacedBy(opened));
    }

    [Fact]
    public void AnotherInodeOrAMissingFileIsAReplacement()
    {
        var opened = new StoreFileIdentity(7, 42, 100, BirthTicks: 5_000);

        Assert.True(opened.IsReplacedBy(new StoreFileIdentity(7, 43, 100, BirthTicks: 5_000)));
        Assert.True(opened.IsReplacedBy(new StoreFileIdentity(8, 42, 100, BirthTicks: 5_000)));
        Assert.True(opened.IsReplacedBy(null));
    }
}
