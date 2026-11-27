using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class WorkerStopCommandTests : IDisposable
{
    private const string Started = "2026-11-20T10:00:00.0000000+00:00";
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.Parse(Started);

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-worker-stop-" + Guid.NewGuid().ToString("N"));

    public WorkerStopCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AProjectNamedStopsEveryRunnerHoldingItsStoreAndRemovesTheirRecords()
    {
        var project = Project("held");
        var store = Path.Combine(_root, "held.motif.db");
        WriteHolder(store, 4101, "build-a", "/root/a", Started);
        WriteHolder(store, 4102, "build-b", "/root/b", "2026-11-20T11:00:00.0000000+00:00");
        var processes = new FakeProcesses
        {
            Names = { [4101] = "SIL.Motif.Worker", [4102] = "SIL.Motif.Worker" },
            Starts = { [4102] = StartedAt.AddHours(1) },
        };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Equal([4101, 4102], processes.Terminated);
        Assert.Equal([("build-a", "/root/a", "stopped"), ("build-b", "/root/b", "stopped")],
            stopped.Value!.Workers.Select(worker => (worker.Build, worker.Root, worker.Outcome)));
        Assert.Empty(Directory.GetFiles(_root, "*.holder-*"));
    }

    [Fact]
    public void WithNoProjectItStopsTheRunnerTheWorkerRootRecords()
    {
        const int pid = 4001;
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\nthe build\n" + Started);
        var processes = new FakeProcesses { Names = { [pid] = "SIL.Motif.Worker" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(), _root, processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Equal([pid], processes.Terminated);
        var worker = Assert.Single(stopped.Value!.Workers);
        Assert.Equal(("the build", _root, "stopped"), (worker.Build, worker.Root, worker.Outcome));
    }

    [Fact]
    public void ARecordedPidThatIsNoLongerARunnerIsLeftAlone()
    {
        var project = Project("reused");
        var store = Path.Combine(_root, "reused.motif.db");
        WriteHolder(store, 4201, "build-a", "/root/a", Started);
        var processes = new FakeProcesses { Names = { [4201] = "bash" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Empty(processes.Terminated);
        Assert.Equal("skipped", Assert.Single(stopped.Value!.Workers).Outcome);
    }

    [Fact]
    public void ALivePidWhoseStartTimeDiffersIsSkippedWithAReasonAndAKillHintAndKeepsItsRecord()
    {
        var project = Project("recycled");
        var store = Path.Combine(_root, "recycled.motif.db");
        WriteHolder(store, 4401, "build-a", "/root/a", Started);
        var processes = new FakeProcesses { Names = { [4401] = "SIL.Motif.Worker" }, Starts = { [4401] = StartedAt.AddHours(3) } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Empty(processes.Terminated);
        var worker = Assert.Single(stopped.Value!.Workers);
        Assert.Equal("skipped", worker.Outcome);
        Assert.Contains("process identity changed (pid reused or clock stepped)", worker.Note);
        Assert.Contains("kill 4401", worker.Note);
        Assert.Contains("taskkill /PID 4401 /F", worker.Note);
        Assert.Single(Directory.GetFiles(_root, "*.holder-*"));
    }

    [Fact]
    public void AReusedPidInTheRunnerIdentityIsSkippedAndTheRecordKept()
    {
        File.WriteAllText(RunnerIdentity.PathFor(_root), "4402\nthe build\n" + Started);
        var processes = new FakeProcesses { Names = { [4402] = "SIL.Motif.Worker" }, Starts = { [4402] = StartedAt.AddDays(1) } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(), _root, processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal("skipped", Assert.Single(stopped.Value!.Workers).Outcome);
        Assert.True(File.Exists(RunnerIdentity.PathFor(_root)));
    }

    [Fact]
    public void ARecordWithoutAParseableStartTimeOnALivePidIsRefusedWithAKillHintAndKept()
    {
        var project = Project("legacy");
        var store = Path.Combine(_root, "legacy.motif.db");
        WriteHolder(store, 4403, "build-a", "/root/a", "unknown");
        var processes = new FakeProcesses { Names = { [4403] = "SIL.Motif.Worker" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal(RefusalCodes.WorkerStopFailed, stopped.Refusal!.Code);
        Assert.Equal("4403", stopped.Refusal.Facts["pids"]);
        Assert.Contains("SIL.Motif.Worker", stopped.Refusal.Message);
        Assert.Contains("kill 4403", stopped.Refusal.Message);
        Assert.Contains("taskkill /PID 4403 /F", stopped.Refusal.Message);
        Assert.Single(Directory.GetFiles(_root, "*.holder-*"));
    }

    [Fact]
    public void AProcessWhoseStartTimeCannotBeReadIsRefusedNotKilled()
    {
        var project = Project("opaque");
        var store = Path.Combine(_root, "opaque.motif.db");
        WriteHolder(store, 4404, "build-a", "/root/a", Started);
        var processes = new FakeProcesses { Names = { [4404] = "SIL.Motif.Worker" } };
        processes.Unreadable.Add(4404);

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal(RefusalCodes.WorkerStopFailed, stopped.Refusal!.Code);
        Assert.Equal("4404", stopped.Refusal.Facts["pids"]);
    }

    [Fact]
    public void TheParentProcessIsNeverStopped()
    {
        var project = Project("parent");
        var store = Path.Combine(_root, "parent.motif.db");
        WriteHolder(store, 4405, "build-a", "/root/a", Started);
        var processes = new FakeProcesses { Names = { [4405] = "SIL.Motif.Worker" }, Parent = 4405 };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal("skipped", Assert.Single(stopped.Value!.Workers).Outcome);
    }

    [Fact]
    public void NoRunnerIsASuccessWithNothingStopped()
    {
        var project = Project("idle");

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), _root, new FakeProcesses());

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Empty(stopped.Value!.Workers);
    }

    [Fact]
    public void AProjectNamedAlsoStopsARunnerOfAnotherBuildOnTheWorkerRootThatHoldsNoStoreRecord()
    {
        var project = Project("otherbuild");
        const int pid = 4501;
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\nthe old build\n" + Started);
        var processes = new FakeProcesses { Names = { [pid] = "SIL.Motif.Worker" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), _root, processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
        Assert.Equal([pid], processes.Terminated);
        var worker = Assert.Single(stopped.Value!.Workers);
        Assert.Equal((pid, "the old build", _root, "stopped"), (worker.Pid, worker.Build, worker.Root, worker.Outcome));
        Assert.False(File.Exists(RunnerIdentity.PathFor(_root)));
    }

    [Fact]
    public void AProjectNamedLeavesARunnerOfThisBuildOnTheRootAloneWhenItHoldsNoStoreRecord()
    {
        var project = Project("samebuild");
        const int pid = 4502;
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\n" + RunnerIdentity.BuildId + "\n" + Started);
        var processes = new FakeProcesses { Names = { [pid] = "SIL.Motif.Worker" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), _root, processes);

        Assert.Empty(processes.Terminated);
        Assert.Empty(stopped.Value!.Workers);
    }

    [Fact]
    public void ARootRecordWhosePidNowBelongsToAnotherProcessIsNotKilledAndItsRecordIsPruned()
    {
        var project = Project("stalepid");
        const int pid = 4503;
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\nthe old build\n" + Started);
        var processes = new FakeProcesses { Names = { [pid] = "bash" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), _root, processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal("skipped", Assert.Single(stopped.Value!.Workers).Outcome);
        Assert.False(File.Exists(RunnerIdentity.PathFor(_root)));
    }

    [Fact]
    public void ARootRecordWhosePidWasReusedByAnotherRunnerStartIsNotKilled()
    {
        var project = Project("reusedroot");
        const int pid = 4504;
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\nthe old build\n" + Started);
        var processes = new FakeProcesses { Names = { [pid] = "SIL.Motif.Worker" }, Starts = { [pid] = StartedAt.AddDays(2) } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), _root, processes);

        Assert.Empty(processes.Terminated);
        Assert.Equal("skipped", Assert.Single(stopped.Value!.Workers).Outcome);
        Assert.True(File.Exists(RunnerIdentity.PathFor(_root)));
    }

    [Fact]
    public void ARunnerThatDoesNotExitIsRefusedNamingItsPid()
    {
        var project = Project("stuck");
        var store = Path.Combine(_root, "stuck.motif.db");
        WriteHolder(store, 4301, "build-a", "/root/a", Started);
        var processes = new FakeProcesses { Names = { [4301] = "SIL.Motif.Worker" }, Survivors = { 4301 } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.False(stopped.Succeeded);
        Assert.Equal(RefusalCodes.WorkerStopFailed, stopped.Refusal!.Code);
        Assert.Equal("4301", stopped.Refusal.Facts["pids"]);
    }

    [Fact]
    public void AMissingProjectIsRefusedAsNotFound()
    {
        var stopped = WorkerStopCommand.Run(
            new WorkerStopRequest(Path.Combine(_root, "nope.fwdata")), "/unused", new FakeProcesses());

        Assert.Equal("project.not-found", stopped.Refusal!.Code);
    }

    private string Project(string name)
    {
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "<languageproject/>");
        return path;
    }

    private static void WriteHolder(string store, int pid, string build, string root, string started) =>
        File.WriteAllText(StoreHolder.PathFor(store, pid), string.Join("\n", pid, build, root, started));

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class FakeProcesses : IWorkerProcessControl
    {
        public Dictionary<int, string> Names { get; } = [];
        public Dictionary<int, DateTimeOffset> Starts { get; } = [];
        public HashSet<int> Unreadable { get; } = [];
        public int? Parent { get; init; }
        public int? ParentPid => Parent;
        public HashSet<int> Survivors { get; } = [];
        public List<int> Terminated { get; } = [];

        public DateTimeOffset? StartTimeOf(int pid) => Unreadable.Contains(pid) ? null : Starts.TryGetValue(pid, out var at) ? at : StartedAt;

        public string? NameOf(int pid) => Names.GetValueOrDefault(pid);

        public bool Terminate(int pid, TimeSpan patience)
        {
            Terminated.Add(pid);
            return !Survivors.Contains(pid);
        }
    }
}
