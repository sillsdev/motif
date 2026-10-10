using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Help;

namespace SIL.Motif.Mcp;

internal static class CatalogTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly HelpCatalog Help = HelpCatalog.Load();

    internal static IReadOnlyList<AgentTool> Build(IReadOnlyList<AgentTool> adapters)
    {
        var eligible = CommandCatalog.All.Where(command => command.IsAgentTool).ToArray();
        var tools = new List<AgentTool>();
        var served = new HashSet<Type>();
        foreach (var adapter in adapters)
        {
            var commands = eligible.Where(command => adapter.Requests.Contains(command.RequestType)).ToArray();
            if (adapter.Requests.Count > 0 && commands.Length == 0) continue;
            if (commands.Length != adapter.Requests.Count)
                throw new InvalidOperationException($"Tool '{adapter.Name}' mixes eligible and unavailable commands.");
            foreach (var command in commands) served.Add(command.RequestType);
            var description = commands.Length == 0 ? ReadDescription(adapter.Name) : Describe(commands);
            tools.Add(Prepare(adapter with { Description = description, Requests = commands.Select(c => c.RequestType).ToArray() }));
        }
        foreach (var command in eligible.Where(command => !served.Contains(command.RequestType)))
            tools.Add(Prepare(Create(command)));
        return tools;
    }

    private static AgentTool Prepare(AgentTool tool)
    {
        var schema = tool.InputSchema.DeepClone().AsObject();
        if (tool.NeedsProject && tool.Name is not ("motif_guide" or "motif_list_projects"))
        {
            schema["properties"]!.AsObject()["project"] = Schema.String("Known project name or recorded .fwdata path.");
            schema["required"] ??= new JsonArray();
            schema["required"]!.AsArray().Add("project");
        }
        return tool with { InputSchema = schema };
    }

    internal static string Name(CommandDescriptor command) => "motif_" +
        Regex.Replace(command.Name.Replace("--", ""), "[^a-zA-Z0-9]+", "_").Trim('_');

    private static string ReadDescription(string name)
    {
        var entry = Help.Find(HelpEntryKind.Command, name);
        if (entry is null) throw new InvalidOperationException($"Tool '{name}' needs shared Help text.");
        return entry.Title + ". " + entry.Description;
    }

    internal static string Describe(IEnumerable<CommandDescriptor> commands) => string.Join("\n", commands.Select(command =>
    {
        var entry = Help.Find(HelpEntryKind.Command, command.Name)
            ?? throw new InvalidOperationException($"Command '{command.Name}' needs shared Help text.");
        var fields = command.RequestType.GetProperties().Where(property => !IsContext(property.Name))
            .Select(property => FieldDoc(command.RequestType, property)).Where(value => value.Length > 0);
        return entry.Title + ". " + entry.Description + "\nCommand: " + command.Name + ". " +
            "Agent class: " + command.Agent + ".\n" + string.Join("\n", fields);
    }));

    private static string FieldDoc(Type type, PropertyInfo property)
    {
        var path = Path.ChangeExtension(type.Assembly.Location, ".xml");
        if (!File.Exists(path)) return string.Empty;
        var document = XDocument.Load(path);
        var member = document.Descendants("member").FirstOrDefault(element =>
            (string?)element.Attribute("name") == "T:" + type.FullName);
        var text = member?.Elements("param").FirstOrDefault(element =>
            (string?)element.Attribute("name") == property.Name)?.Value;
        text ??= document.Descendants("member").FirstOrDefault(element =>
            (string?)element.Attribute("name") == "P:" + type.FullName + "." + property.Name)?.Element("summary")?.Value;
        return text is null ? string.Empty : property.Name + ": " + Regex.Replace(text.Trim(), @"\s+", " ");
    }

    private static bool IsContext(string name) => name is "ProjectPath" or "FwDataPath" or "ProductVersion";

    private static AgentTool Create(CommandDescriptor command)
    {
        _ = Handler(command);
        var schema = Schema.Object(command.RequestType.GetProperties().Where(property => !IsContext(property.Name))
            .Select(property => (JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                FieldSchema(property.PropertyType), Required(command.RequestType, property))).ToArray());
        return new AgentTool(Name(command), Describe([command]), command.Agent, schema,
            command.Agent == AgentClass.Read, command.Agent == AgentClass.Read, command.Name == "assess", false, true,
            (context, args, cancellation) => Invoke(command, context, args, cancellation))
        { Requests = [command.RequestType], NeedsProject = command.RequestType.GetProperties().Any(p =>
            p.Name is "ProjectPath" or "FwDataPath") };
    }

    private static bool Required(Type type, PropertyInfo property) =>
        new NullabilityInfoContext().Create(property).ReadState != NullabilityState.Nullable && type.GetConstructors()
        .OrderByDescending(constructor => constructor.GetParameters().Length).First().GetParameters()
        .FirstOrDefault(parameter => string.Equals(parameter.Name, property.Name, StringComparison.OrdinalIgnoreCase))
        is { HasDefaultValue: false };

    private static JsonObject FieldSchema(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type == typeof(TimeSpan) || type == typeof(DateTimeOffset) || type == typeof(DateTime) || type == typeof(Guid))
            return Schema.String("Text in the request field format.");
        if (type == typeof(string)) return Schema.String("Value for the request field.");
        if (type == typeof(bool)) return Schema.Boolean("Value for the request field.");
        if (type.IsPrimitive) return new JsonObject { ["type"] = type == typeof(double) || type == typeof(float) ? "number" : "integer" };
        if (type.IsEnum) return Schema.String("Declared choice.", Enum.GetNames(type));
        if (type != typeof(string) && type.GetInterfaces().Append(type).Any(candidate => candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
        {
            var element = type.IsArray ? type.GetElementType()! : type.GetGenericArguments()[0];
            return Schema.Array("Declared items.", FieldSchema(element));
        }
        return Schema.Object(type.GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => (JsonNamingPolicy.CamelCase.ConvertName(p.Name), FieldSchema(p.PropertyType), false)).ToArray());
    }

    internal static MethodInfo Handler(CommandDescriptor command)
    {
        return typeof(CommandCatalog).Assembly.GetTypes().Where(type => type.IsAbstract && type.IsSealed)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(method => method.GetParameters().FirstOrDefault()?.ParameterType == command.RequestType &&
                method.ReturnType.IsGenericType && method.ReturnType.GetGenericArguments()[0] == command.ResponseType &&
                method.GetParameters().Skip(1).All(parameter => parameter.HasDefaultValue))
            .OrderBy(method => method.GetParameters().Length).FirstOrDefault()
            ?? throw new InvalidOperationException($"No typed handler found for '{command.Name}'.");
    }

    private static async Task<ToolOutcome> Invoke(CommandDescriptor command, ServerContext context, ToolArgs args,
        CancellationToken cancellation)
    {
        var json = args.Copy();
        json.Remove("project");
        foreach (var property in command.RequestType.GetProperties().Where(p => IsContext(p.Name)))
            json[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = property.Name == "ProductVersion"
                ? context.ProductVersion : context.ProjectPath;
        foreach (var property in command.RequestType.GetProperties().Where(property => !IsContext(property.Name) &&
                     Required(command.RequestType, property)))
            if (!json.ContainsKey(JsonNamingPolicy.CamelCase.ConvertName(property.Name)))
                throw new ToolArgumentException("tool.missing-argument", $"Argument '{property.Name}' is required.");
        object request;
        try { request = json.Deserialize(command.RequestType, Json)!; }
        catch (JsonException exception) { throw new ToolArgumentException("tool.invalid-argument", exception.Message); }
        var method = Handler(command);
        var parameters = method.GetParameters().Select((parameter, index) => index == 0 ? request :
            parameter.ParameterType == typeof(CancellationToken) ? cancellation :
            parameter.Name == "parserPath" ? context.ParserPath : parameter.DefaultValue).ToArray();
        var outcome = await Task.Run(() => method.Invoke(null, parameters)!, cancellation);
        var refusal = outcome.GetType().GetProperty("Refusal")!.GetValue(outcome) as Refusal;
        if (refusal is not null) return ToolOutcome.Refused(refusal);
        var value = outcome.GetType().GetProperty("Value")!.GetValue(outcome);
        if (value is JobEnqueuedResponse job)
        {
            context.StartRunner();
            return ToolOutcome.Ok(JsonSerializer.SerializeToNode(job, Json)!, jobId: job.JobId);
        }
        return ToolOutcome.Ok(JsonSerializer.SerializeToNode(value, Json)!);
    }
}
