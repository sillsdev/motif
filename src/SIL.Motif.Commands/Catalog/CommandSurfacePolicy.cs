using System;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// Decides whether a catalogued command is available for one invocation surface. The opt-ins are supplied
/// by the process boundary so this policy remains deterministic for callers and tests.
/// </summary>
public sealed class CommandSurfacePolicy
{
    /// <summary>The opt-in environment variable that enables developer-only commands.</summary>
    public const string DeveloperCommandsEnvironmentVariable = "MOTIF_DEVELOPER_COMMANDS";

    /// <summary>Creates a policy with the supplied command opt-ins.</summary>
    public CommandSurfacePolicy(bool developerCommandsEnabled, bool advancedAiModeEnabled = false)
    {
        DeveloperCommandsEnabled = developerCommandsEnabled;
        AdvancedAiModeEnabled = advancedAiModeEnabled;
    }

    /// <summary>Whether this policy exposes developer-only commands.</summary>
    public bool DeveloperCommandsEnabled { get; }

    /// <summary>Whether Advanced AI mode commands are available.</summary>
    public bool AdvancedAiModeEnabled { get; }

    /// <summary>Whether <paramref name="command"/> is available on this surface.</summary>
    public bool IsAvailable(CommandDescriptor command) =>
        command.Surface switch
        {
            CommandSurface.Released => true,
            CommandSurface.Developer => DeveloperCommandsEnabled,
            CommandSurface.AdvancedAi => AdvancedAiModeEnabled,
            _ => false,
        };

    /// <summary>Creates a policy from the already-read opt-in environment value.</summary>
    public static CommandSurfacePolicy FromEnvironment(string? value, bool advancedAiModeEnabled = false) =>
        new(string.Equals(value, "1", StringComparison.Ordinal), advancedAiModeEnabled);
}
