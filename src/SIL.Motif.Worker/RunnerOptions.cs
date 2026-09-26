using System;
using System.Globalization;
using System.IO;
using SIL.Motif.Host.Parser;

namespace SIL.Motif.Worker;

/// <summary>What one job runner process was told about where to work and how long to hold a job.</summary>
/// <remarks>
/// Explicit launch arguments bind a worker to the root and parser selected by its caller. Environment values
/// remain defaults for a worker started directly or by a command-line invocation.
/// </remarks>
public sealed record RunnerOptions
{
    /// <summary>Explicitly selects the worker root for a launched process.</summary>
    public const string RootArgument = "--root";

    /// <summary>Explicitly selects the parser for a launched process.</summary>
    public const string ParserArgument = "--parser";

    /// <summary>Explicitly disables parser selection for a launched process.</summary>
    public const string NoParserArgument = "--no-parser";

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
    /// How long the runner stays alive with nothing to do, in seconds. A runner the CLI spawned takes no
    /// arguments, so this is the only way to tune one that was not started by hand — an operator shortening
    /// the wait on a machine that idles badly, or a caller that wants a kicked runner to go away promptly.
    /// <c>--idle-ms</c> still wins where it is passed.
    /// </summary>
    public const string IdleVariable = "MOTIF_RUNNER_IDLE_SECONDS";

    public string Root { get; init; } = ResolveRoot();

    /// <summary>The parser path passed to this worker, or null when no parser was selected.</summary>
    public string? ParserPath { get; init; }

    public TimeSpan Lease { get; init; } = TimeSpan.FromMinutes(5);

    public string? OwnerNamespace { get; init; }

    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Reads explicit launch arguments first, then the runner's environment defaults.</summary>
    public static RunnerOptions Read(string[] args) => new()
    {
        Root = ArgumentValue(args, RootArgument) ?? ResolveRoot(),
        ParserPath = HasArgument(args, NoParserArgument)
            ? null
            : ArgumentValue(args, ParserArgument) ?? PanGlossExecutable.TryLocate(),
        Lease = Seconds(Value(LeaseVariable)) ?? TimeSpan.FromMinutes(5),
        OwnerNamespace = Value(NamespaceVariable),
        IdleTimeout = IdleFrom(args) ?? Seconds(Value(IdleVariable)) ?? TimeSpan.FromMinutes(5),
    };

    /// <summary>The worker root any process (runner or CLI) uses: <see cref="RootVariable"/>, or the per-user default.</summary>
    public static string ResolveRoot() => Value(RootVariable) ?? DefaultRoot;

    /// <summary>The per-user root used when no command-line configuration supplies another location.</summary>
    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SIL", "Motif");

    private static string? Value(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value;
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

    private static TimeSpan? IdleFrom(string[] args)
    {
        for (var index = 0; index + 1 < args.Length; index++)
            if (string.Equals(args[index], "--idle-ms", StringComparison.Ordinal) &&
                int.TryParse(args[index + 1], out var milliseconds) && milliseconds > 0)
                return TimeSpan.FromMilliseconds(milliseconds);
        return null;
    }
}
