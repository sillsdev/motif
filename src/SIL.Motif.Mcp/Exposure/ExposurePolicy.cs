using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp.Exposure;

/// <summary>
/// The one place that decides which tools an agent sees and which tool, with which selecting argument, reaches
/// a catalogued command. It reads the settings, the profile and the routes each tool declares by command name.
/// </summary>
internal sealed class ExposurePolicy
{
    private readonly IReadOnlyList<ExposedTool> _tools;

    private ExposurePolicy(ExposureSettings settings, IReadOnlyList<ExposedTool> tools)
    {
        Settings = settings;
        _tools = tools;
        var table = new Dictionary<string, CommandExposure>(StringComparer.Ordinal);
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var exposed in tools)
            foreach (var route in exposed.Tool.Routes)
            {
                var candidate = new CommandExposure.Tool(exposed.Name, route.Selector, [.. exposed.Tool.ArgumentNames]);
                // The route's own rank decides: a tool that is the command beats one reached by a selecting argument.
                if (!ranks.TryGetValue(route.Command, out var held) || route.Rank < held)
                {
                    table[route.Command] = candidate;
                    ranks[route.Command] = route.Rank;
                }
            }
        RouteTable = table;
    }

    /// <summary>The settings this policy was made for.</summary>
    public ExposureSettings Settings { get; }

    /// <summary>The tools the model sees, in a stable order; none unless Advanced AI mode is on.</summary>
    public IReadOnlyList<ExposedTool> Tools => _tools;

    /// <summary>Server instructions for the model, or the setup text when Advanced AI mode is off.</summary>
    public string? Instructions => Settings.Surfaces.AdvancedAiModeEnabled
        ? Settings.Profile.Instructions
        : FileAdvancedAiModePreferenceStore.DisabledServerInstructions;

    /// <summary>Each routed command and the exposed tool that reaches it.</summary>
    public IReadOnlyDictionary<string, CommandExposure> RouteTable { get; }

    /// <summary>Builds the policy for <paramref name="settings"/> over every tool the server can offer.</summary>
    public static ExposurePolicy Create(ExposureSettings settings, IReadOnlyList<AgentTool> tools) =>
        new(settings, settings.Surfaces.AdvancedAiModeEnabled ? Select(settings.Profile, tools) : []);

    /// <summary>A policy over tools already chosen, for a caller that holds the exposed list and not the profile.</summary>
    internal static ExposurePolicy ForExposed(IReadOnlyList<ExposedTool> exposed) =>
        new(new ExposureSettings(new CommandSurfacePolicy(false, true), ToolProfile.Builtin), exposed);

    /// <summary>Whether a tool in this policy reaches <paramref name="command"/>.</summary>
    public bool Exposes(string command) => RouteTable.ContainsKey(command);

    /// <summary>How to reach <paramref name="command"/>: a tool, a step for the person, or a missing opt-in.</summary>
    public CommandExposure Resolve(string command)
    {
        if (RouteTable.TryGetValue(command, out var exposure)) return exposure;
        var descriptor = CommandCatalog.All.Single(candidate => candidate.Name == command);
        return Settings.Surfaces.IsAvailable(descriptor)
            ? new CommandExposure.PersonStep(command)
            : new CommandExposure.Unavailable(CommandAvailabilityFacts.For(descriptor));
    }

    /// <summary>The refusal every tool call gets while Advanced AI mode is off, or <see langword="null"/> when it is on.</summary>
    public static ToolOutcome? CallGate(bool advancedAiModeEnabled) => advancedAiModeEnabled
        ? null
        : ToolOutcome.Refused("advanced-ai.disabled", FailureReason.Refused, FileAdvancedAiModePreferenceStore.DisabledMessage);

    /// <summary>Whether an agent may ever be offered a command or composer of this surface and class.</summary>
    public static bool IsAgentEligible(CommandSurface surface, AgentClass agent) =>
        CommandSurfacePolicy.IsAgentEligible(surface, agent);

    private static List<ExposedTool> Select(ToolProfile profile, IReadOnlyList<AgentTool> tools)
    {
        var known = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        foreach (var selection in profile.Tools)
            if (!known.ContainsKey(selection.Tool))
                throw new ProfileException($"Profile '{profile.Name}' names tool '{selection.Tool}', which does not exist. " +
                    $"Tools are: {string.Join(", ", known.Keys.Order(StringComparer.Ordinal))}.");
        var exposed = new List<ExposedTool>();
        foreach (var tool in tools)
            if (profile.SelectionFor(tool.Name) is { } selection)
                exposed.Add(new ExposedTool(tool, selection.As ?? tool.Name, selection.Description ?? tool.Description,
                    selection.Detail ?? profile.DefaultDetail));
        var duplicate = exposed.GroupBy(tool => tool.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ProfileException($"Profile '{profile.Name}' gives two tools the name '{duplicate.Key}'.");
        return exposed;
    }
}
