using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;

namespace SIL.Motif.Commands;

/// <summary>What a launched job runner is told: where to work, which parser to use, and how to run.</summary>
/// <remarks>
/// These settings reach only a runner that the launch itself starts. The runner is a per-user singleton, so
/// a launch that finds one already running leaves it alone, and that runner keeps the settings it started
/// with.
/// </remarks>
/// <param name="Root">The root directory whose Known projects and machine database the runner uses.</param>
/// <param name="ParserPath">The parser a Trial runs, or <see langword="null"/> to run no Trials.</param>
public sealed record JobRunnerLaunchOptions(string Root, string? ParserPath)
{
    /// <summary>The runner executable, or <see langword="null"/> for the one beside the running host.</summary>
    public string? WorkerExecutable { get; init; }

    /// <summary>
    /// Isolates the runner's per-user ownership mutex, or <see langword="null"/> for the shared one. A test
    /// sets it so its runner never waits behind the developer's own.
    /// </summary>
    public string? OwnerNamespace { get; init; }

    /// <summary>How long the runner stays alive with nothing to do, or <see langword="null"/> for its default.</summary>
    public TimeSpan? IdleTimeout { get; init; }

    /// <summary>How long a claimed job is held, or <see langword="null"/> for the runner's default.</summary>
    public TimeSpan? Lease { get; init; }

    /// <summary>
    /// The settings a process's environment selects: the root <see cref="RunnerOptions.ResolveRoot"/> reads,
    /// the parser <see cref="PanGlossExecutable.TryLocate()"/> finds, and any configured runner executable.
    /// The command line and the installed window both start from these.
    /// </summary>
    public static JobRunnerLaunchOptions FromEnvironment() => new(
        RunnerOptions.ResolveRoot(), PanGlossExecutable.TryLocate())
    {
        WorkerExecutable = ProcessRunnerLauncher.ConfiguredExecutable(),
    };
}
