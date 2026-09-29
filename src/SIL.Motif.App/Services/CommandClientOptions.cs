using SIL.Motif.Commands;

namespace SIL.Motif.App.Services;

/// <summary>Supplies the App's explicit parser, worker root, and worker launcher.</summary>
/// <param name="ManagedRoot">The worker root every command the window runs uses.</param>
/// <param name="ParserPath">The parser the window's commands run, or <see langword="null"/> for none.</param>
/// <param name="RunnerLauncher">
/// Starts the runner for queued work. Its <see cref="IJobRunnerLauncher.Options"/> must name the same root
/// and parser; the client refuses one that names another, pinned by
/// `ALauncherForAnotherRootOrParserIsRefused`.
/// </param>
/// <param name="StartGate">Holds an Assessment or Handoff before it starts, or <see langword="null"/>.</param>
/// <param name="TimeProvider">The clock synchronous command captures use, or <see langword="null"/> for system time.</param>
public sealed record CommandClientOptions(
    string ManagedRoot,
    string? ParserPath,
    IJobRunnerLauncher RunnerLauncher,
    ICommandStartGate? StartGate = null,
    TimeProvider? TimeProvider = null)
{
    /// <summary>
    /// The installed window's settings, resolved once from the environment exactly as the command line
    /// resolves them (<see cref="ProcessRunnerLauncher.FromEnvironment"/>), so by default the window and the
    /// CLI name one worker root and one parser (ADR 0040). The runner is a per-user singleton: one already
    /// running, started with other settings, keeps them, and the window's launch starts no second one, pinned
    /// by `KickingWhenARunnerIsAlreadyAliveDoesNotStartASecondOwner`.
    /// </summary>
    public static CommandClientOptions ForInstallation()
    {
        var runner = ProcessRunnerLauncher.FromEnvironment();
        return new CommandClientOptions(runner.Options.Root, runner.Options.ParserPath, runner);
    }
}
