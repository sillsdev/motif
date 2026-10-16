using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Mcp.Exposure;

/// <summary>Derives each tool's routes from the catalog, so the command a tool serves is stated once.</summary>
internal static class ToolRoutes
{
    internal const string BatchSelector = "intents[].verb";
    internal const string ComposeSelector = "steps[].verb";

    /// <summary>Routes for the catalogued commands <paramref name="tool"/> is built over, or its own when it has them.</summary>
    internal static AgentTool WithRoutes(AgentTool tool, IReadOnlyList<string> batchVerbs)
    {
        if (tool.Routes.Count > 0) return tool;
        var selector = tool.Name switch
        {
            "motif_compose_batch" => BatchSelector,
            "motif_compose" => ComposeSelector,
            _ => null,
        };
        if (selector is not null)
            return tool with { Routes = [.. batchVerbs.Select(verb => new ToolRoute(verb,
                new ToolSelector(selector, verb), ClassOf(verb)))] };
        // A typed composer's tool names no request type; its generator supplies the verb route instead.
        var routes = tool.Requests.SelectMany(request => CommandCatalog.All
                .Where(command => command.RequestType == request && !ComposerVerbs.Contains(command.Name)))
            .Select(command => new ToolRoute(command.Name, null, command.Agent)).ToArray();
        return tool with { Routes = routes };
    }

    private static readonly IReadOnlySet<string> ComposerVerbs = ComposerTools.Verbs;

    private static AgentClass ClassOf(string verb) => CommandCatalog.All.Single(command => command.Name == verb).Agent;
}
