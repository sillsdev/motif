using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class WorkerStopCommandTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-worker-stop-" + Guid.NewGuid().ToString("N"));

    public WorkerStopCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AProjectNamedStopsEveryRunnerHoldingItsStoreAndRemovesTheirRecords()
    {
        var project = Project("held");
        var store = Path.Combine(_root, "held.motif.db");
        WriteHolder(store, 4101, "build-a", "/root/a", "2026-11-20T10:00:00.0000000+00:00");
        WriteHolder(store, 4102, "build-b", "/root/b", "2026-11-20T11:00:00.0000000+00:00");
        var processes = new FakeProcesses { Names = { [4101] = "SIL.Motif.Worker", [4102] = "SIL.Motif.Worker" } };

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
        File.WriteAllText(RunnerIdentity.PathFor(_root), pid + "\nthe build");
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
        WriteHolder(store, 4201, "build-a", "/root/a", "2026-11-20T10:00:00.0000000+00:00");
        var processes = new FakeProcesses { Names = { [4201] = "bash" } };

        var stopped = WorkerStopCommand.Run(new WorkerStopRequest(project), "/unused", processes);

        Assert.True(stopped.Succeeded, stopped.Refusal?.Message);
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
    public void ARunnerThatDoesNotExitIsRefusedNamingItsPid()
    {
        var project = Project("stuck");
        var store = Path.Combine(_root, "stuck.motif.db");
        WriteHolder(store, 4301, "build-a", "/root/a", "2026-11-20T10:00:00.0000000+00:00");
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
        public HashSet<int> Survivors { get; } = [];
        public List<int> Terminated { get; } = [];

        public string? NameOf(int pid) => Names.GetValueOrDefault(pid);

        public bool Terminate(int pid, TimeSpan patience)
        {
            Terminated.Add(pid);
            return !Survivors.Contains(pid);
        }
    }
}
