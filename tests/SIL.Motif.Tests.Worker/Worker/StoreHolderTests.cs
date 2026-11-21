using System.Diagnostics;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class StoreHolderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "motif-holder-" + Guid.NewGuid().ToString("N"));

    public StoreHolderTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void AHolderFileNamesTheLiveRunnerAndAFileForADeadProcessIsRemoved()
    {
        var store = Path.Combine(_directory, "demo.motif.db");
        using var mark = StoreHolder.Publish(store, "/the/root");
        var deadPid = DeadPid();
        File.WriteAllText(StoreHolder.PathFor(store, deadPid),
            string.Join("\n", deadPid, "old build", "/other/root", "2026-01-01T00:00:00.0000000+00:00"));

        var holders = StoreHolder.ReadLive(store);

        var holder = Assert.Single(holders);
        Assert.Equal(Environment.ProcessId, holder.Pid);
        Assert.Equal("/the/root", holder.Root);
        Assert.Equal(RunnerIdentity.BuildId, holder.Build);
        Assert.False(File.Exists(StoreHolder.PathFor(store, deadPid)));
    }

    [Fact]
    public void DisposingTheMarkRemovesTheRecord()
    {
        var store = Path.Combine(_directory, "demo.motif.db");
        var mark = StoreHolder.Publish(store, "/the/root");
        Assert.True(File.Exists(StoreHolder.PathFor(store, Environment.ProcessId)));

        mark.Dispose();

        Assert.Empty(StoreHolder.ReadLive(store));
        Assert.False(File.Exists(StoreHolder.PathFor(store, Environment.ProcessId)));
    }

    [Fact]
    public void ARuntimeOpenedUnderARootRecordsItselfUntilItIsReleased()
    {
        var project = new ProjectLocator(Path.Combine(_directory, "demo.fwdata"), "project");
        var store = ProjectDatabaseCatalog.DatabasePathFor(project);
        var ownership = WorkspaceOwnership.Bootstrap(Path.Combine(_directory, "owned"));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0), "/runner/root");
        using var registry = new ProjectRuntimeRegistry(catalog,
            (jobs, key) => new WorkerRecoveryCoordinator(new WorkerRecovery(jobs), new WorkspaceCleaner(ownership)),
            new ProjectRuntimeActivity());

        var runtime = registry.GetOrOpen(project);

        var holder = Assert.Single(StoreHolder.ReadLive(store));
        Assert.Equal("/runner/root", holder.Root);
        Assert.True(registry.TryReleaseIfIdle(runtime.WorkspaceKey));
        Assert.Empty(StoreHolder.ReadLive(store));
    }

    [Fact]
    public void ARuntimeOpenedWithoutARootLeavesNoRecord()
    {
        var project = new ProjectLocator(Path.Combine(_directory, "plain.fwdata"), "project");
        var ownership = WorkspaceOwnership.Bootstrap(Path.Combine(_directory, "owned"));
        var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0));
        using var registry = new ProjectRuntimeRegistry(catalog,
            (jobs, key) => new WorkerRecoveryCoordinator(new WorkerRecovery(jobs), new WorkspaceCleaner(ownership)),
            new ProjectRuntimeActivity());

        registry.GetOrOpen(project);

        Assert.Empty(StoreHolder.ReadLive(ProjectDatabaseCatalog.DatabasePathFor(project)));
    }

    [Fact]
    public void TheRunnerIdentityOfARootIsReadOnlyWhileItsProcessIsAlive()
    {
        var root = Path.Combine(_directory, "root");
        Assert.Null(RunnerIdentity.ReadLive(root));

        RunnerIdentity.Publish(root);
        Assert.Equal((Environment.ProcessId, RunnerIdentity.BuildId, RunnerProcess.SelfStartText), RunnerIdentity.ReadLive(root));

        var deadPid = DeadPid();
        File.WriteAllText(RunnerIdentity.PathFor(root), deadPid + "\nold build");
        Assert.Null(RunnerIdentity.ReadLive(root));
    }

    [Fact]
    public void DisposingAnOlderMarkLeavesTheRecordOfItsReplacement()
    {
        var store = Path.Combine(_directory, "demo.motif.db");
        var older = StoreHolder.Publish(store, "/old/root");
        using var newer = StoreHolder.Publish(store, "/new/root");

        older.Dispose();

        Assert.Equal("/new/root", Assert.Single(StoreHolder.ReadLive(store)).Root);
    }

    [Fact]
    public void AHolderRecordIsNamedAfterThisProcessStartTime()
    {
        var store = Path.Combine(_directory, "demo.motif.db");
        using var mark = StoreHolder.Publish(store, "/the/root");

        var holder = Assert.Single(StoreHolder.ReadLive(store));

        Assert.True(RunnerProcess.SameStart(holder.StartedUtc, RunnerProcess.StartOf(Environment.ProcessId)!.Value));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp-holder-*"));
    }

    [Fact]
    public void AnUnreadableRecordOfALiveProcessIsKeptAndOneOfADeadProcessIsRemoved()
    {
        var store = Path.Combine(_directory, "demo.motif.db");
        var live = StoreHolder.PathFor(store, Environment.ProcessId);
        var deadPid = DeadPid();
        var dead = StoreHolder.PathFor(store, deadPid);
        File.WriteAllText(live, "");
        File.WriteAllText(dead, "");

        Assert.Empty(StoreHolder.ReadLive(store));

        Assert.True(File.Exists(live));
        Assert.False(File.Exists(dead));
    }

    [Theory]
    [InlineData("2026-11-20T10:00:01.0000000+00:00", true)]
    [InlineData("2026-11-20T10:00:09.0000000+00:00", false)]
    [InlineData("", false)]
    [InlineData("garbage", false)]
    public void ARecordedStartMatchesOnlyWithinASmallTolerance(string recorded, bool expected) =>
        Assert.Equal(expected, RunnerProcess.SameStart(recorded, DateTimeOffset.Parse("2026-11-20T10:00:00Z")));

    private static int DeadPid()
    {
        var start = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/c exit 0")
            : new ProcessStartInfo("true");
        start.UseShellExecute = false;
        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.Id;
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
