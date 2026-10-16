using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Mcp.Exposure;

/// <summary>Derives each tool's routes from the catalog, so the command a tool serves is stated once.</summary>
internal static class ToolRoutes
{
    internal const string BatchSelector = "intents[].verb";
    internal const string ComposeSelector = "steps[].verb";

    /// <summary>A tool that is the command.</summary>
    internal const int DedicatedRank = 0;

    /// <summary>The compose tool, which a step reaches ahead of the batch.</summary>
    internal const int ComposeStepRank = 1;

    /// <summary>The batch tool, the fallback for a verb with no other tool.</summary>
    internal const int BatchIntentRank = 2;

    /// <summary>The routes of <paramref name="tool"/>: its composer verb, the verbs it selects, or its request types' commands.</summary>
    internal static AgentTool Derive(AgentTool tool, IReadOnlyList<string> batchVerbs)
    {
        if (tool.ComposerVerb is { } verb) return tool with { Routes = [new ToolRoute(verb, null, tool.Class, DedicatedRank)] };
        if (tool.VerbSelector is { } selector)
        {
            var rank = selector == ComposeSelector ? ComposeStepRank : BatchIntentRank;
            return tool with { Routes = [.. batchVerbs.Select(batchVerb => new ToolRoute(batchVerb,
                new ToolSelector(selector, batchVerb), ClassOf(batchVerb), rank))] };
        }
        var routes = tool.Requests.SelectMany(request => CommandCatalog.All
                .Where(command => command.RequestType == request && !ComposerVerbs.Contains(command.Name)))
            .Select(command => new ToolRoute(command.Name, null, command.Agent, DedicatedRank)).ToArray();
        return tool with { Routes = routes };
    }

    private static readonly IReadOnlySet<string> ComposerVerbs = ComposerTools.Verbs;

    private static AgentClass ClassOf(string verb) => CommandCatalog.All.Single(command => command.Name == verb).Agent;
}
