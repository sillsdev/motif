using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;

namespace SIL.Motif.Mcp;

/// <summary>
/// Builds the AI tools and batch intent schemas of composers declared as typed definitions. A definition
/// supplies the verb, shape and exposure; Help supplies the description and every argument's meaning, so adding a
/// composer needs no tool code.
/// </summary>
internal static class ComposerTools
{
    private const string DraftArgument = "draft";
    private static readonly HelpCatalog Help = HelpCatalog.Load();

    /// <summary>The commands these tools serve, which the generic catalog path must not also expose.</summary>
    internal static IReadOnlySet<string> Verbs { get; } =
        ProposalCommands.ComposerDescriptors.Select(composer => composer.Verb).ToHashSet(StringComparer.Ordinal);

    /// <summary>The verbs a batch may name that are declared as typed definitions.</summary>
    internal static IEnumerable<string> BatchVerbs =>
        ProposalCommands.ComposerDescriptors.Where(composer => composer.InBatch).Select(composer => composer.Verb);

    /// <summary>One tool for each typed definition an agent may use.</summary>
    internal static IReadOnlyList<AgentTool> Build() => ProposalCommands.ComposerDescriptors
        .Where(composer => composer.IsAgentTool).Select(composer => CatalogTools.Prepare(Create(composer))).ToArray();

    /// <summary>The closed schema of one batch entry's <c>intent</c> for a typed definition.</summary>
    internal static (string Verb, JsonObject Schema) BatchIntentSchema(ComposerDescriptor composer)
    {
        var shape = composer.Schema();
        var intent = new JsonObject
        {
            ["type"] = "object",
            ["description"] = $"Structured intent for {composer.Verb}.",
            ["properties"] = Described(composer, shape),
            ["additionalProperties"] = false,
        };
        if (shape["required"] is JsonArray required) intent["required"] = required.DeepClone();
        return (composer.Verb, intent);
    }

    private static AgentTool Create(ComposerDescriptor composer)
    {
        var entry = Help.Find(HelpEntryKind.Command, composer.Verb)
            ?? throw new InvalidOperationException($"Composer '{composer.Verb}' needs shared Help text.");
        var shape = composer.Schema();
        var properties = new JsonObject
        {
            [DraftArgument] = Schema.String(FieldText(composer, entry, DraftArgument)),
        };
        foreach (var (name, schema) in Described(composer, shape)) properties[name] = schema?.DeepClone();
        var required = new JsonArray { DraftArgument };
        if (shape["required"] is JsonArray fields)
            foreach (var field in fields) required.Add(field!.DeepClone());
        var inputSchema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required,
            ["additionalProperties"] = false };
        var description = entry.Title + ". " + entry.Description + "\nCommand: " + composer.Verb + ". " +
            "Agent class: " + composer.Agent + ".";
        var verb = composer.Verb;
        var names = composer.FieldNames;
        return new AgentTool(composer.ToolName, description, composer.Agent, inputSchema, false, false,
            composer.OnByDefault, false, true,
            (context, args, cancellation) => Task.FromResult(ToolOutcome.From(
                ProposalCommands.ComposeIntent(new ComposeIntentRequest(context.ProjectPath, context.ProductVersion,
                    args.Required(DraftArgument), verb, args.IntentJson([.. names]))),
                result => "Use createdObjects[].id values in later composer calls; Dry Run and Trial the Draft, then Finalize.")))
        { Routes = [new Exposure.ToolRoute(verb, null, composer.Agent)] };
    }

    private static JsonObject Described(ComposerDescriptor composer, JsonObject shape)
    {
        var entry = Help.Find(HelpEntryKind.Command, composer.Verb)
            ?? throw new InvalidOperationException($"Composer '{composer.Verb}' needs shared Help text.");
        var properties = new JsonObject();
        foreach (var (name, schema) in shape["properties"]!.AsObject())
        {
            var described = schema!.DeepClone().AsObject();
            described["description"] = FieldText(composer, entry, name);
            properties[name] = described;
        }
        return properties;
    }

    private static string FieldText(ComposerDescriptor composer, HelpEntry entry, string field) =>
        entry.Fields is { } fields && fields.TryGetValue(field, out var text)
            ? text
            : throw new InvalidOperationException($"Help for '{composer.Verb}' does not describe field '{field}'.");
}
