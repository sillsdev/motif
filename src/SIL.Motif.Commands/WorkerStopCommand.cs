using System.Diagnostics;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// The runner to stop: the one that holds <c>ProjectPath</c>'s store open, or, when no project is named, the one
/// the current worker root records.
/// </summary>
public sealed record WorkerStopRequest(string? ProjectPath = null);

/// <summary>One runner process <c>worker stop</c> found and what happened to it.</summary>
/// <param name="Pid">The runner's process id.</param>
/// <param name="Build">The build the runner records, or empty when none is recorded.</param>
/// <param name="Root">The worker root the runner serves, or empty when none is recorded.</param>
/// <param name="Outcome"><c>stopped</c>, or <c>skipped</c> when the pid no longer names a Motif runner.</param>
public sealed record StoppedWorker(int Pid, string Build, string Root, string Outcome);

/// <summary>The runners <c>worker stop</c> found, and the worker root and project it looked under.</summary>
public sealed record WorkerStopResponse(string Root, string? ProjectPath, IReadOnlyList<StoppedWorker> Workers);

/// <summary>Reads and ends runner processes; the seam <see cref="WorkerStopCommand"/> is tested through.</summary>
public interface IWorkerProcessControl
{
    /// <summary>The name of the live process <paramref name="pid"/>, or <see langword="null"/> when none is running.</summary>
    string? NameOf(int pid);

    /// <summary>Ends <paramref name="pid"/> and returns whether it had exited within <paramref name="patience"/>.</summary>
    bool Terminate(int pid, TimeSpan patience);

    /// <summary>When the live process <paramref name="pid"/> started, or null when it is gone or unreadable.</summary>
    DateTimeOffset? StartTimeOf(int pid);

    /// <summary>The parent of this process, or null when the system does not say.</summary>
    int? ParentPid { get; }
}

/// <summary>Ends the background runner that holds a project store or the current worker root.</summary>
public static class WorkerStopCommand
{
    private static readonly TimeSpan ExitPatience = TimeSpan.FromSeconds(10);

    /// <summary>Stops the runner the request names, using the operating system's process table.</summary>
    public static CommandOutcome<WorkerStopResponse> Run(WorkerStopRequest request) =>
        Run(request, RunnerOptions.ResolveRoot(), new SystemWorkerProcessControl());

    internal static CommandOutcome<WorkerStopResponse> Run(WorkerStopRequest request, string root,
        IWorkerProcessControl processes)
    {
        ArgumentNullException.ThrowIfNull(request);
        var targets = new List<(StoppedWorker Worker, string Started)>();
        string? storePath = null;
        string? projectPath = null;
        if (request.ProjectPath is { } named)
        {
            var located = ProjectStoreCommand.LocateProject(named);
            if (!located.Succeeded) return CommandOutcome<WorkerStopResponse>.Refused(located.Refusal!);
            projectPath = located.Value!.FullFwDataPath;
            storePath = ProjectDatabaseCatalog.DatabasePathFor(located.Value);
            foreach (var holder in StoreHolder.ReadLive(storePath, pid => processes.NameOf(pid) is not null))
                targets.Add((new StoppedWorker(holder.Pid, holder.Build, holder.Root, "stopped"), holder.StartedUtc));
        }
        else if (RunnerIdentity.ReadLive(root, pid => processes.NameOf(pid) is not null) is { } runner)
        {
            targets.Add((new StoppedWorker(runner.Pid, runner.Build, root, "stopped"), runner.StartedUtc));
        }

        var results = new List<StoppedWorker>();
        var failed = new List<int>();
        var unverified = new List<int>();
        foreach (var (target, started) in targets.DistinctBy(item => item.Worker.Pid))
        {
            if (target.Pid == Environment.ProcessId || target.Pid == processes.ParentPid)
            {
                results.Add(target with { Outcome = "skipped" });
                continue;
            }
            var name = processes.NameOf(target.Pid);
            if (name is null || !LooksLikeRunner(name))
            {
                Prune(storePath, root, target.Pid);
                results.Add(target with { Outcome = "skipped" });
                continue;
            }
            if (processes.StartTimeOf(target.Pid) is not { } actual)
            {
                unverified.Add(target.Pid);
                continue;
            }
            if (!RunnerProcess.SameStart(started, actual))
            {
                Prune(storePath, root, target.Pid);
                results.Add(target with { Outcome = "skipped" });
                continue;
            }
            if (processes.Terminate(target.Pid, ExitPatience))
            {
                Prune(storePath, root, target.Pid);
                results.Add(target);
            }
            else
            {
                failed.Add(target.Pid);
            }
        }

        if (unverified.Count != 0)
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pids"] = string.Join(",", unverified),
                ["stopped"] = string.Join(",", results.Where(item => item.Outcome == "stopped").Select(item => item.Pid)),
            };
            if (projectPath is not null) facts["fwDataPath"] = projectPath;
            return CommandOutcome<WorkerStopResponse>.Refused(new Refusal(RefusalCodes.WorkerStopFailed, FailureReason.Refused,
                "The process (pid " + string.Join(", ", unverified) + ") could not be confirmed as the recorded runner, " +
                "so it was not stopped. Stop it from the operating system if it is the runner.", facts));
        }

        if (failed.Count != 0)
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["pids"] = string.Join(",", failed),
                ["stopped"] = string.Join(",", results.Where(item => item.Outcome == "stopped").Select(item => item.Pid)),
            };
            if (projectPath is not null) facts["fwDataPath"] = projectPath;
            return CommandOutcome<WorkerStopResponse>.Refused(new Refusal(RefusalCodes.WorkerStopFailed, FailureReason.Refused,
                "The runner (pid " + string.Join(", ", failed) + ") did not exit within " +
                (int)ExitPatience.TotalSeconds + " seconds of being stopped. Stop it from the operating system, " +
                "or run `motif worker stop` again.", facts));
        }

        return CommandOutcome<WorkerStopResponse>.Success(new WorkerStopResponse(root, projectPath, results));
    }

    private static void Prune(string? storePath, string root, int pid)
    {
        if (storePath is not null) StoreHolder.Remove(storePath, pid);
        else RunnerIdentity.Remove(root, pid);
    }

    private static bool LooksLikeRunner(string processName)
    {
        var names = new List<string> { "SIL.Motif.Worker" };
        if (ProcessRunnerLauncher.ConfiguredExecutable() is { } configured)
            names.Add(Path.GetFileNameWithoutExtension(configured));
        return names.Any(name => processName.StartsWith(name, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SystemWorkerProcessControl : IWorkerProcessControl
    {
        public string? NameOf(int pid)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                return process.HasExited ? null : process.ProcessName;
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return null;
            }
        }

        public DateTimeOffset? StartTimeOf(int pid) => RunnerProcess.StartOf(pid);

        public int? ParentPid { get; } = ReadParentPid();

        private static int? ReadParentPid()
        {
            try
            {
                var stat = File.ReadAllText("/proc/self/stat");
                var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return int.Parse(fields[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                FormatException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                return null;
            }
        }

        public bool Terminate(int pid, TimeSpan patience)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                process.Kill();
                return process.WaitForExit(patience);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
