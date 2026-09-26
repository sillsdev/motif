using SIL.Motif.Commands;

namespace SIL.Motif.App.Services;

/// <summary>Supplies the App's explicit parser, worker root, and worker launcher.</summary>
/// <param name="ManagedRoot">The worker root every command the window runs uses.</param>
/// <param name="ParserPath">The parser the window's commands run, or <see langword="null"/> for none.</param>
/// <param name="RunnerLauncher">
/// Starts the runner for queued work. Its <see cref="IJobRunnerLauncher.Options"/> must name the same root
/// and parser, so the window and its runner never work in two places.
/// </param>
public sealed record CommandClientOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher)
{
    /// <summary>
    /// The installed window's settings, resolved once from the environment exactly as the command line
    /// resolves them (<see cref="ProcessRunnerLauncher.FromEnvironment"/>), so the window and the CLI share
    /// one worker root and one parser (ADR 0040).
    /// </summary>
    public static CommandClientOptions ForInstallation()
    {
        var runner = ProcessRunnerLauncher.FromEnvironment();
        return new CommandClientOptions(runner.Options.Root, runner.Options.ParserPath, runner);
    }
}
