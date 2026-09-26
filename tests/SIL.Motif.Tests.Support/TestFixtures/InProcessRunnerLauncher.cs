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
/// <remarks>
/// <see cref="Start"/> returns at once and drains in the background, as starting a runner process does, so
/// the caller reaches its own wait and can cancel it or time out. A cancellation request reaches a running
/// job through the loop's heartbeat, every third of <see cref="JobRunnerLaunchOptions.Lease"/>. Drains run
/// one at a time. The owner awaits <see cref="WhenIdleAsync"/> or disposes the launcher before deleting the
/// project, which also surfaces any exception a drain threw.
/// </remarks>
public sealed class InProcessRunnerLauncher : IJobRunnerLauncher, IAsyncDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly SemaphoreSlim _oneDrainAtATime = new(1, 1);
    private readonly List<Task> _drains = [];
    private readonly object _drainsLock = new();

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
        var drain = Task.Run(() => DrainAsync(full, _stopping.Token));
        lock (_drainsLock) _drains.Add(drain);
    }

    /// <summary>Completes when every drain started so far has finished, rethrowing what any of them threw.</summary>
    public Task WhenIdleAsync()
    {
        Task[] drains;
        lock (_drainsLock) drains = [.. _drains];
        return Task.WhenAll(drains);
    }

    /// <summary>Cancels the running job, if any, and waits for every drain to finish.</summary>
    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        try
        {
            await WhenIdleAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DrainAsync(string projectPath, CancellationToken stopping)
    {
        await _oneDrainAtATime.WaitAsync(stopping).ConfigureAwait(false);
        try
        {
            var project = new ProjectLocator(projectPath, Path.GetFileNameWithoutExtension(projectPath));
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var runner = new RunnerOptions
            {
                Root = Options.Root,
                ParserPath = Options.ParserPath,
                Lease = Options.Lease ?? TimeSpan.FromMinutes(1),
            };

            using var database = ProjectMotifDatabase.Open(projectPath);
            var baselines = new BaselineRepository(database);
            using var lanes = new ProjectLaneRegistry(key => baselines.GetCurrent(key)?.Token ??
                throw new InvalidOperationException("The project has no Baseline to order work against."));
            using var invoker = new PanGlossInvoker(Options.ParserPath);
            var loop = ProjectJobHandlers.CreateLoop(database, baselines, workspaceKey, project, runner, invoker,
                "in-process-runner", lanes);
            await loop.RunUntilIdleAsync(stopping).ConfigureAwait(false);
        }
        finally
        {
            _oneDrainAtATime.Release();
        }
    }
}
