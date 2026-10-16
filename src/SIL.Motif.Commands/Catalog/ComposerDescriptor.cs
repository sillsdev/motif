using System.Text.Json.Nodes;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// What a front end needs to offer one typed composer definition: its verb, where it is offered, and its closed
/// intent shape. The definition itself stays internal to the commands assembly.
/// </summary>
public sealed class ComposerDescriptor
{
    private readonly JsonObject _schema;

    internal ComposerDescriptor(IComposerDefinition definition)
    {
        var shape = definition.Shape ?? throw new InvalidOperationException(
            $"Composer '{definition.Verb}' is declared as a typed definition without a shape.");
        var exposure = definition.Exposure ?? throw new InvalidOperationException(
            $"Composer '{definition.Verb}' is declared as a typed definition without an exposure.");
        Verb = definition.Verb;
        Surface = exposure.Surface;
        Agent = exposure.Agent;
        OnByDefault = exposure.OnByDefault;
        AgentTool = exposure.AgentTool;
        InBatch = exposure.InBatch && exposure.AgentTool;
        ToolName = exposure.ToolName ?? ToolNameFor(Verb);
        FieldNames = shape.Fields.Select(field => field.Name).ToArray();
        _schema = shape.ToJsonSchema();
    }

    /// <summary>The CLI verb, and the <c>verb</c> a batch entry names.</summary>
    public string Verb { get; }

    /// <summary>The user choice that makes the composer available.</summary>
    public CommandSurface Surface { get; }

    /// <summary>What an AI agent may do with the composer.</summary>
    public AgentClass Agent { get; }

    /// <summary>Whether the composer's AI tool is registered without the user naming it.</summary>
    public bool OnByDefault { get; }

    /// <summary>Whether AI agents are offered the composer; false keeps it CLI-first.</summary>
    public bool AgentTool { get; }

    /// <summary>Whether a batch may name the composer's verb.</summary>
    public bool InBatch { get; }

    /// <summary>The AI tool's name.</summary>
    public string ToolName { get; }

    /// <summary>The top-level intent property names, in declaration order.</summary>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>Whether the composer is offered as an AI tool at all.</summary>
    public bool IsAgentTool => AgentTool && CommandSurfacePolicy.IsAgentEligible(Surface, Agent);

    /// <summary>A fresh copy of the closed JSON Schema of the intent.</summary>
    public JsonObject Schema() => _schema.DeepClone().AsObject();

    /// <summary>
    /// The tool name the verb implies: <c>compose-author-x-y</c> becomes <c>motif_add_x_y</c> and
    /// <c>compose-edit-x-y</c> becomes <c>motif_edit_x_y</c>. Any other verb must name its tool explicitly.
    /// </summary>
    internal static string ToolNameFor(string verb)
    {
        foreach (var (prefix, action) in new[] { ("compose-author-", "add"), ("compose-edit-", "edit") })
            if (verb.StartsWith(prefix, StringComparison.Ordinal))
                return $"motif_{action}_{verb[prefix.Length..].Replace('-', '_')}";
        throw new InvalidOperationException($"Verb '{verb}' needs an explicit tool name in its exposure.");
    }
}
