using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Projects;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public void AStoreAtTheSameInodeWithAnotherStoreIdIsReplacedEvenWithNoBirthTime()
    {
        var opened = new StoreFileIdentity(7, 42, 100, StoreId: "aaa");

        Assert.True(opened.IsReplacedBy(new StoreFileIdentity(7, 42, 100, StoreId: "bbb")));
    }

    [Fact]
    public void AnEqualStoreIdNeverHidesAnotherFile()
    {
        var opened = new StoreFileIdentity(7, 42, 100, StoreId: "aaa");

        Assert.False(opened.IsReplacedBy(new StoreFileIdentity(7, 42, 100, StoreId: "aaa")));
        Assert.True(opened.IsReplacedBy(new StoreFileIdentity(7, 43, 100, StoreId: "aaa")));
    }

    [Fact]
    public void AMissingStoreIdOnEitherSideLeavesTheFileSignalsToDecide()
    {
        var opened = new StoreFileIdentity(7, 42, 100, StoreId: "aaa");
        var unread = new StoreFileIdentity(7, 42, 100);

        Assert.False(opened.IsReplacedBy(unread));
        Assert.False(unread.IsReplacedBy(opened));
        Assert.True(unread.IsReplacedBy(new StoreFileIdentity(7, 43, 100, StoreId: "bbb")));
    }

    [Fact]
    public void AStoreRecreatedAtAReusedInodeOnAFileSystemWithoutBirthTimeIsAReplacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-store-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "store.motif");
            var project = new ProjectLocator(Path.Combine(root, "p.fwdata"), "project-guid");
            using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
            var opened = StoreFileIdentity.Read(path)!.Value;
            Assert.NotNull(opened.StoreId);
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
            var recreated = StoreFileIdentity.Read(path)!.Value;

            // Look as a reused inode would: same device and inode, no birth time.
            var atTheReusedInode = recreated with { Volume = opened.Volume, File = opened.File, BirthTicks = 0 };
            Assert.NotEqual(opened.StoreId, recreated.StoreId);
            Assert.True((opened with { BirthTicks = 0 }).IsReplacedBy(atTheReusedInode));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AStoreMadeBeforeTheIdExistedGainsOneOnItsNextWritableOpenWithoutAnUpgrade()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-store-id-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "store.motif");
            var project = new ProjectLocator(Path.Combine(root, "p.fwdata"), "project-guid");
            using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
            SqliteConnection.ClearAllPools();
            using (var old = new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                old.Open();
                using var drop = old.CreateCommand();
                drop.CommandText = "DROP TABLE MotifStoreIdentity;";
                drop.ExecuteNonQuery();
            }
            Assert.Null(MotifDatabase.TryReadStoreId(path));
            MotifDatabase.ReadExisting(path, project, (_, _) => 0);

            using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
            var gained = MotifDatabase.TryReadStoreId(path);
            Assert.NotNull(gained);
            SqliteConnection.ClearAllPools();
            using (MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0))) { }
            Assert.Equal(gained, MotifDatabase.TryReadStoreId(path));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
