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

    /// <summary>The verbs a compose step may name that are declared as typed definitions.</summary>
    internal static IEnumerable<string> ComposeVerbs =>
        ProposalCommands.ComposerDescriptors.Where(composer => composer.InCompose).Select(composer => composer.Verb);

    /// <summary>The description a tool for a typed composer shows: its Help title and description, then its command.</summary>
    internal static string Describe(string verb)
    {
        var composer = ProposalCommands.ComposerDescriptors.Single(candidate => candidate.Verb == verb);
        var entry = Help.Find(HelpEntryKind.Command, verb)
            ?? throw new InvalidOperationException($"Composer '{verb}' needs shared Help text.");
        return entry.Title + ". " + (entry.McpCallout is { } line ? line + " " : string.Empty) + entry.Description + "\nCommand: " + verb + ". " + "Agent class: " + composer.Agent + ".";
    }

    /// <summary>One tool for each typed definition an agent may use and that has no hand-written tool.</summary>
    internal static IReadOnlyList<AgentTool> Build() => ProposalCommands.ComposerDescriptors
        .Where(composer => composer.HasGeneratedTool).Select(composer => CatalogTools.Prepare(Create(composer))).ToArray();

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
        if (shape["allOf"] is JsonArray constraints) intent["allOf"] = constraints.DeepClone();
        return (composer.Verb, intent);
    }

    private static AgentTool Create(ComposerDescriptor composer)
    {
        var entry = Help.Find(HelpEntryKind.Command, composer.Verb)
            ?? throw new InvalidOperationException($"Composer '{composer.Verb}' needs shared Help text.");
        var shape = SnakeCaseNames(composer.Schema());
        var properties = new JsonObject
        {
            [DraftArgument] = Schema.String(FieldText(composer, entry, DraftArgument)),
        };
        foreach (var (name, schema) in Described(composer, shape, snake: true)) properties[name] = schema?.DeepClone();
        var required = new JsonArray { DraftArgument };
        if (shape["required"] is JsonArray fields)
            foreach (var field in fields) required.Add(field!.DeepClone());
        var inputSchema = new JsonObject { ["type"] = "object", ["properties"] = properties, ["required"] = required,
            ["additionalProperties"] = false };
        if (shape["allOf"] is JsonArray constraints) inputSchema["allOf"] = constraints.DeepClone();
        var description = Describe(composer.Verb);
        var verb = composer.Verb;
        var names = composer.FieldNames;
        return new AgentTool(composer.ToolName, description, composer.Agent, inputSchema, false, false,
            false, true,
            (context, args, cancellation) => Task.FromResult(SourceAiGuard.Check(args.Required(DraftArgument), verb) is { } needsProposal
                ? ToolOutcome.Refused(needsProposal)
                : ToolOutcome.From(
                ProposalCommands.ComposeIntent(new ComposeIntentRequest(context.ProjectPath, context.ProductVersion,
                    args.Required(DraftArgument), verb, args.IntentJson([.. names]))),
                result => "Use createdObjects[].id values in later composer calls; Dry Run and Trial the Draft, then Finalize.")))
        { ComposerVerb = verb };
    }

    /// <summary>The intent shape with top-level names in snake_case; nested names stay camelCase.</summary>
    private static JsonObject SnakeCaseNames(JsonObject shape)
    {
        var result = shape.DeepClone().AsObject();
        var properties = new JsonObject();
        foreach (var (name, schema) in shape["properties"]!.AsObject()) properties[Schema.Snake(name)] = schema?.DeepClone();
        result["properties"] = properties;
        if (shape["required"] is JsonArray required)
            result["required"] = new JsonArray(required.Select(name => (JsonNode)Schema.Snake(name!.GetValue<string>())).ToArray());
        if (shape["dependentRequired"] is JsonObject dependent)
        {
            var renamed = new JsonObject();
            foreach (var (name, others) in dependent)
                renamed[Schema.Snake(name)] = new JsonArray(others!.AsArray()
                    .Select(other => (JsonNode)Schema.Snake(other!.GetValue<string>())).ToArray());
            result["dependentRequired"] = renamed;
        }
        if (shape["allOf"] is JsonArray exclusions)
            result["allOf"] = new JsonArray(exclusions.Select(exclusion =>
            {
                var copy = exclusion!.DeepClone().AsObject();
                if (copy["not"]?["required"] is JsonArray pair)
                    copy["not"]!["required"] = new JsonArray(pair.Select(name => (JsonNode)Schema.Snake(name!.GetValue<string>())).ToArray());
                return (JsonNode)copy;
            }).ToArray());
        return result;
    }

    private static JsonObject Described(ComposerDescriptor composer, JsonObject shape, bool snake = false)
    {
        var entry = Help.Find(HelpEntryKind.Command, composer.Verb)
            ?? throw new InvalidOperationException($"Composer '{composer.Verb}' needs shared Help text.");
        var properties = new JsonObject();
        foreach (var (name, schema) in shape["properties"]!.AsObject())
        {
            var described = schema!.DeepClone().AsObject();
            var text = FieldText(composer, entry, snake ? Schema.Camel(name) : name);
            if (snake)
                foreach (var field in composer.FieldNames.Where(field => Schema.Snake(field) != field))
                    text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{field}\b", Schema.Snake(field));
            described["description"] = text;
            properties[name] = described;
        }
        return properties;
    }

    private static string FieldText(ComposerDescriptor composer, HelpEntry entry, string field) =>
        entry.Fields is { } fields && fields.TryGetValue(field, out var text)
            ? text
            : throw new InvalidOperationException($"Help for '{composer.Verb}' does not describe field '{field}'.");
}
