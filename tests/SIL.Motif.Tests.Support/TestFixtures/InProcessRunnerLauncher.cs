using SIL.Motif.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Drains one project's queue inside the test process, through the handlers the runner process builds
/// (<see cref="ProjectJobHandlers"/>): Dry Runs, Baseline refreshes, and Trials with
/// <see cref="JobRunnerLaunchOptions.ParserPath"/>. It never records a Baseline of its own; a Dry Run
/// queued before one exists parks, exactly as it does in the runner.
/// </summary>
public sealed class InProcessRunnerLauncher : IJobRunnerLauncher
{
    public InProcessRunnerLauncher(JobRunnerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Root);
        Options = options;
    }

    public JobRunnerLaunchOptions Options { get; }

    public void Start(string projectPath, Action<string>? reportWarning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var full = Path.GetFullPath(projectPath);
        var project = new ProjectLocator(full, Path.GetFileNameWithoutExtension(full));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var runner = new RunnerOptions
        {
            Root = Options.Root,
            ParserPath = Options.ParserPath,
            Lease = Options.Lease ?? TimeSpan.FromMinutes(1),
        };

        using var database = ProjectMotifDatabase.Open(full);
        var baselines = new BaselineRepository(database);
        using var lanes = new ProjectLaneRegistry(key => baselines.GetCurrent(key)?.Token ??
            throw new InvalidOperationException("The project has no Baseline to order work against."));
        using var invoker = new PanGlossInvoker(Options.ParserPath);
        var loop = ProjectJobHandlers.CreateLoop(database, baselines, workspaceKey, project, runner, invoker,
            "in-process-runner", lanes);
        loop.RunUntilIdleAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
