using System;
using System.Globalization;
using System.IO;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker;

/// <summary>What one job runner process was told about where to work and how long to hold a job.</summary>
/// <remarks>
/// Launch arguments configure the runner that a launch starts, and win over the environment, which stays
/// the default for a runner started by hand. The runner is a per-user singleton: a launch that finds one
/// already running starts nothing, and the running one keeps the settings it started with.
/// <see cref="Read"/> and <see cref="ResolveRoot"/> consult the environment, pinned by
/// `ResolveRootAndReadTakeTheRootVariable`, and an argument wins over it, pinned by
/// `ExplicitArgumentsSelectTheRootParserNamespaceIdleAndLease`. Options built in code carry exactly what they
/// are given, pinned by `OptionsBuiltInCodeReadNothingFromTheEnvironment`.
/// </remarks>
public sealed record RunnerOptions
{
    /// <summary>Explicitly selects the root directory for a launched process.</summary>
    public const string RootArgument = "--root";

    /// <summary>Explicitly selects the parser for a launched process.</summary>
    public const string ParserArgument = "--parser";

    /// <summary>Explicitly disables parser selection for a launched process.</summary>
    public const string NoParserArgument = "--no-parser";

    /// <summary>Isolates a launched process's owner mutex, as <see cref="NamespaceVariable"/> does.</summary>
    public const string NamespaceArgument = "--namespace";

    /// <summary>The project whose durable enqueue caused this runner launch.</summary>
    public const string WakeProjectArgument = "--wake-project";

    /// <summary>A launched process's idle timeout, in milliseconds.</summary>
    public const string IdleArgument = "--idle-ms";

    /// <summary>A launched process's job lease, in milliseconds.</summary>
    public const string LeaseArgument = "--lease-ms";

    /// <summary>Relocates everything the runner owns. An operator needs this to run two installations.</summary>
    public const string RootVariable = "MOTIF_WORKER_ROOT";

    /// <summary>How long a claimed job is held before another runner may take it back.</summary>
    public const string LeaseVariable = "MOTIF_RUNNER_LEASE_SECONDS";

    /// <summary>
    /// Isolates this runner's owner mutex. <b>Test-only.</b>
    /// </summary>
    /// <remarks>
    /// It exists because a runner started by a test would otherwise contend for the same per-user mutex as
    /// the developer's real runner, and two concurrent test runs would contend with each other. No
    /// operator has a reason to set it: two real installations are separated by
    /// <see cref="RootVariable"/>, which is about where work happens rather than who may do it.
    /// </remarks>
    public const string NamespaceVariable = "MOTIF_RUNNER_NAMESPACE";

    /// <summary>
    /// How long the runner stays alive with nothing to do, in seconds. A kicked runner inherits its caller's
    /// environment, so an operator shortening the wait on a machine that idles badly sets it there.
    /// <see cref="IdleArgument"/>, which a launcher passes when its options name an idle timeout, still wins.
    /// </summary>
    public const string IdleVariable = "MOTIF_RUNNER_IDLE_SECONDS";

    /// <summary>The root directory whose Known projects and machine database this runner uses.</summary>
    public required string Root { get; init; }

    /// <summary>The parser a Trial runs, or <see langword="null"/> when this runner runs no Trials.</summary>
    public string? ParserPath { get; init; }

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(5);

    public string? OwnerNamespace { get; init; }

    public string? WakeProjectPath { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Reads explicit launch arguments first, then the runner's environment defaults.</summary>
    /// <remarks>
    /// A parser named by argument must exist, and a blank one selects none, as <see cref="NoParserArgument"/>
    /// does: a runner registers its Trial handler only for a parser it can start.
    /// </remarks>
    public static RunnerOptions Read(string[] args) => new()
    {
        Root = ArgumentValue(args, RootArgument) ?? ResolveRoot(),
        ParserPath = ParserFrom(args),
        Lease = Milliseconds(ArgumentValue(args, LeaseArgument)) ?? Seconds(Value(LeaseVariable)) ??
            TimeSpan.FromMinutes(5),
        OwnerNamespace = ArgumentValue(args, NamespaceArgument) ?? Value(NamespaceVariable),
        WakeProjectPath = ArgumentValue(args, WakeProjectArgument),
        IdleTimeout = Milliseconds(ArgumentValue(args, IdleArgument)) ?? Seconds(Value(IdleVariable)) ??
            TimeSpan.FromMinutes(5),
    };

    /// <summary>The root any process (runner or CLI) uses: <see cref="RootVariable"/>, or the per-user default.</summary>
    public static string ResolveRoot() => Value(RootVariable) ?? DefaultRoot;

    /// <summary>The per-user root used when no command-line configuration supplies another location.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif");

    private static string? Value(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ParserFrom(string[] args)
    {
        if (HasArgument(args, NoParserArgument)) return null;
        if (!HasArgument(args, ParserArgument)) return PanGlossExecutable.TryLocate();
        return ArgumentValue(args, ParserArgument) is { } parser && File.Exists(parser) ? parser : null;
    }

    private static string? ArgumentValue(string[] args, string name)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], name, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(args[index + 1]))
                return args[index + 1];
        return null;
    }

    private static bool HasArgument(string[] args, string name) =>
        Array.Exists(args, argument => string.Equals(argument, name, StringComparison.Ordinal));

    private static TimeSpan? Seconds(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
        seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static TimeSpan? Milliseconds(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds) &&
        milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : null;
}
