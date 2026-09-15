using System;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// Decides whether a catalogued command is available for one invocation surface. The environment value
/// is supplied by the process boundary so this policy remains deterministic for callers and tests.
/// </summary>
public sealed class CommandSurfacePolicy
{
    /// <summary>The opt-in environment variable that enables developer-only commands.</summary>
    public const string DeveloperCommandsEnvironmentVariable = "MOTIF_DEVELOPER_COMMANDS";

    /// <summary>Creates a policy with or without the developer-only command opt-in.</summary>
    public CommandSurfacePolicy(bool developerCommandsEnabled) =>
        DeveloperCommandsEnabled = developerCommandsEnabled;

    /// <summary>Whether this policy exposes developer-only commands.</summary>
    public bool DeveloperCommandsEnabled { get; }

    /// <summary>Whether <paramref name="command"/> is available on this surface.</summary>
    public bool IsAvailable(CommandDescriptor command) =>
        command.Surface == CommandSurface.Released || DeveloperCommandsEnabled;

    /// <summary>Creates a policy from the already-read opt-in environment value.</summary>
    public static CommandSurfacePolicy FromEnvironment(string? value) =>
        new(string.Equals(value, "1", StringComparison.Ordinal));
}
