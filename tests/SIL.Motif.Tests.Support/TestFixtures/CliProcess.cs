using System.Diagnostics;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Starts Motif CLI processes with the same isolated runner configuration across test projects.</summary>
public static class CliProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static ProcessStartInfo CreateStartInfo(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.Environment.Remove("ICU_DATA");
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = workerRoot;
        start.Environment[PanGlossExecutable.PathVariable] = parserPath ?? FakeParser.ExecutablePath;
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        if (developerCommands)
            start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        start.Environment.Remove("FAKE_PANGLOSS_BEHAVIOUR_PATH");
        return start;
    }

    /// <summary>Builds a CLI process with an explicitly isolated Advanced AI mode preference file.</summary>
    public static ProcessStartInfo CreateStartInfoWithAdvancedAiModePath(
        string workerRoot, string? parserPath, bool developerCommands, string preferencePath,
        params string[] arguments)
    {
        var start = CreateStartInfo(workerRoot, parserPath, developerCommands, arguments);
        start.Environment["MOTIF_ADVANCED_AI_MODE_PATH"] = preferencePath;
        return start;
    }

    public static async Task<CliProcessResult> RunAsync(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        using var process = Process.Start(CreateStartInfo(workerRoot, parserPath, developerCommands, arguments))!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(Timeout);
        }
        catch (TimeoutException exception)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { }
            var output = await outputTask;
            var error = await errorTask;
            throw new TimeoutException(
                $"The CLI did not exit within {Timeout}.{Environment.NewLine}" +
                $"Standard error:{Environment.NewLine}{error}{Environment.NewLine}" +
                $"Standard output:{Environment.NewLine}{output}", exception);
        }
        return new CliProcessResult(process.ExitCode, await outputTask, await errorTask);
    }

    /// <summary>Builds a CLI process using the supplied runner and arguments.</summary>
    /// <param name="runner">The worker and parser settings for the test project.</param>
    /// <param name="arguments">The verb and its arguments.</param>
    /// <returns>A redirected process start with developer commands disabled.</returns>
    public static ProcessStartInfo Start(JobRunnerLaunchOptions runner, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = runner.Root;
        start.Environment[ProcessRunnerLauncher.ExecutableVariable] = runner.WorkerExecutable;
        start.Environment[PanGlossExecutable.PathVariable] = runner.ParserPath;
        start.Environment[RunnerOptions.NamespaceVariable] = runner.OwnerNamespace;
        start.Environment[RunnerOptions.IdleVariable] = "1";
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        return start;
    }

    /// <summary>Runs a redirected CLI process and returns its exit code and both output streams.</summary>
    /// <param name="start">The CLI process to run.</param>
    /// <returns>The process exit code, standard output, and standard error.</returns>
    public static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(Timeout);
        }
        catch (TimeoutException exception)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            var output = await outputTask;
            var error = await errorTask;
            throw new TimeoutException(
                $"The CLI did not exit within {Timeout}.{Environment.NewLine}" +
                $"Standard error:{Environment.NewLine}{error}{Environment.NewLine}" +
                $"Standard output:{Environment.NewLine}{output}", exception);
        }
        return (process.ExitCode, await outputTask, await errorTask);
    }

    /// <summary>Starts a worker after the CLI has queued a pending-change Dry Run.</summary>
    /// <param name="projectPath">The project whose queue the CLI populated.</param>
    /// <param name="options">The isolated worker settings.</param>
    /// <param name="cli">The running CLI process to monitor.</param>
    /// <returns>The worker process that drains the queued job.</returns>
    public static async Task<Process> StartQueuedWorkerAsync(
        string projectPath, JobRunnerLaunchOptions options, Process cli) =>
        await TryStartQueuedWorkerAsync(projectPath, options, cli) ?? throw new InvalidOperationException(
            $"The CLI exited with {cli.ExitCode} before queueing a Dry Run for pending changes.");

    /// <summary>
    /// Starts the isolated worker once the CLI queues a Dry Run, or returns null when the CLI exits without
    /// queueing one, leaving its exit code for the caller to judge.
    /// </summary>
    /// <param name="projectPath">The FieldWorks project whose job store is polled.</param>
    /// <param name="options">The isolated worker settings.</param>
    /// <param name="cli">The running CLI process to monitor.</param>
    /// <returns>The worker process that drains the queued job, or null when none was queued.</returns>
    public static async Task<Process?> TryStartQueuedWorkerAsync(
        string projectPath, JobRunnerLaunchOptions options, Process cli)
    {
        while (!cli.HasExited)
        {
            using var database = ProjectMotifDatabase.Open(projectPath);
            if (new JobRepository(database).ListActive().Any(job => job.Kind == JobCommands.DryRunKind))
            {
                var start = new ProcessStartInfo(options.WorkerExecutable!) { UseShellExecute = false };
                foreach (var argument in ProcessRunnerLauncher.LaunchArguments(options))
                    start.ArgumentList.Add(argument);
                return Process.Start(start)!;
            }
            await Task.Delay(20);
        }
        return null;
    }

}

public sealed record CliProcessResult(int ExitCode, string Output, string Error)
{
    public string FailureDetails =>
        $"CLI exited {ExitCode}.{Environment.NewLine}Standard error:{Environment.NewLine}{Error}" +
        $"{Environment.NewLine}Standard output:{Environment.NewLine}{Output}";
}
