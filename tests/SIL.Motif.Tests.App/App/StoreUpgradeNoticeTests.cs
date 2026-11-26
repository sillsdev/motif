using Avalonia.Threading;
using SIL.Motif.Host.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class StoreUpgradeNoticeTests
{
    [Fact]
    public void AnAutomaticUpgradeShowsItsNoticeUntilDismissed()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var workspace = FakeComposedWindow.CreateWorkspace();
            Assert.False(workspace.HasStoreUpgradeNotice);

            StoreUpgradeNotices.Record(new StoreUpgradeNotice("/p/a.motif.db", 62, 79, "/p/backup", "/p/backup/motif-backup.json",
                new Dictionary<string, long> { ["Jobs"] = 2 }));
            Dispatcher.UIThread.RunJobs();

            Assert.True(workspace.HasStoreUpgradeNotice);
            Assert.Contains("upgraded from schema 62 to 79; a backup is at /p/backup", workspace.StoreUpgradeMessage);
            Assert.Contains("Jobs (2)", workspace.StoreUpgradeMessage);
            workspace.DismissStoreUpgradeNoticeCommand.Execute(null);
            Assert.False(workspace.HasStoreUpgradeNotice);
            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(60));
    }
}
