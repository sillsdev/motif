using SIL.Motif.Commands.Preferences;

namespace SIL.Motif.Commands.Catalog;

/// <summary>The catalog facts explaining a command's opt-in and independent agent eligibility.</summary>
public sealed record CommandAvailabilityFacts(
    string Command, CommandSurface Surface, AgentClass Agent, bool AgentEligible, string? Setting, string Remedy)
{
    /// <summary>Reads availability facts from the catalog entry, without changing settings.</summary>
    public static CommandAvailabilityFacts For(CommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command.Surface switch
        {
            CommandSurface.AdvancedAi => new(command.Name, command.Surface, command.Agent, command.IsAgentTool,
                "advanced-ai", FileAdvancedAiModePreferenceStore.DisabledMessage),
            CommandSurface.Developer => new(command.Name, command.Surface, command.Agent, command.IsAgentTool,
                CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable,
                $"Set {CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable}=1 to enable them."),
            CommandSurface.Released => new(command.Name, command.Surface, command.Agent, command.IsAgentTool, null, string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
    }

    /// <summary>The stable refusal code for a disabled opt-in surface.</summary>
    public string RefusalCode => Surface switch
    {
        CommandSurface.AdvancedAi => "command.advanced-ai-mode-required",
        CommandSurface.Developer => "command.developer-commands-required",
        _ => throw new InvalidOperationException("Released commands do not require an opt-in."),
    };

    /// <summary>Projects the facts into the shared refusal-detail shape.</summary>
    public IReadOnlyDictionary<string, string> RefusalFacts()
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["command"] = Command,
            ["surface"] = Surface.ToString(),
            ["agentClass"] = Agent.ToString(),
            ["agentEligible"] = AgentEligible ? "true" : "false",
            ["remedy"] = Remedy,
        };
        if (Setting is not null) facts["setting"] = Setting;
        if (Surface == CommandSurface.AdvancedAi)
        {
            facts["preferencePathEnvironmentVariable"] = FileAdvancedAiModePreferenceStore.PathEnvironmentVariable;
            facts["reconnectRequired"] = "true";
        }
        return facts;
    }
}

/// <summary>Human text for the same catalog facts used in structured command refusals.</summary>
public static class CommandAvailabilityText
{
    /// <summary>Names the opt-in remedy and, independently, any human-only restriction.</summary>
    public static string Render(CommandAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var message = facts.Surface switch
        {
            CommandSurface.AdvancedAi => $"Command '{facts.Command}' requires Advanced AI mode. " + facts.Remedy,
            CommandSurface.Developer => $"Command '{facts.Command}' requires Developer commands. " + facts.Remedy,
            CommandSurface.Released => $"Command '{facts.Command}' is Released.",
            _ => throw new ArgumentOutOfRangeException(nameof(facts)),
        };
        return facts.Agent == AgentClass.HumanOnly
            ? message + " This command is human-only and is never exposed as an MCP tool."
            : message;
    }
}
