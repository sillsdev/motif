using System.Text.Json.Nodes;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>Everything a tool call needs from the server that is not in its arguments.</summary>
internal sealed class ServerContext(string projectPath, string productVersion, IJobRunnerLauncher runner,
    ActivityLog activity, ToolProfile profile, TextWriter log)
{
    public string ProjectPath { get; } = projectPath;

    public string ProductVersion { get; } = productVersion;

    public ActivityLog Activity { get; } = activity;

    public ToolProfile Profile { get; } = profile;

    /// <summary>Where diagnostics go; standard output belongs to the protocol.</summary>
    public TextWriter Log { get; } = log;

    /// <summary>LibLCM locks a project file exclusively, so calls that open it take turns.</summary>
    public SemaphoreSlim ProjectGate { get; } = new(1, 1);

    /// <summary>Wakes the job runner after a job is queued, as every other front end does.</summary>
    public void StartRunner() => runner.Start(ProjectPath, Log.WriteLine);
}

/// <summary>The arguments of one tool call, with the closed-schema checks every tool shares.</summary>
internal sealed class ToolArgs
{
    private readonly JsonObject _arguments;

    public ToolArgs(JsonObject arguments, IEnumerable<string> allowed)
    {
        _arguments = arguments;
        var known = allowed.ToHashSet(StringComparer.Ordinal);
        foreach (var (name, _) in arguments)
            if (!known.Contains(name))
                throw new ToolArgumentException("tool.unknown-argument", $"This tool has no argument '{name}'. " +
                    $"Its arguments are: {string.Join(", ", known.Order(StringComparer.Ordinal))}.");
    }

    public string Required(string name) =>
        Optional(name) ?? throw new ToolArgumentException("tool.missing-argument", $"Argument '{name}' is required.");

    public string? Optional(string name) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    public bool RequiredBoolean(string name)
    {
        if (!_arguments.TryGetPropertyValue(name, out var node) || node is null)
            throw new ToolArgumentException("tool.missing-argument", $"Argument '{name}' is required and must be a Boolean.");
        if (node is JsonValue value && value.TryGetValue<bool>(out var flag)) return flag;
        throw new ToolArgumentException("tool.invalid-argument", $"Argument '{name}' must be a Boolean.");
    }

    public JsonObject RequiredObject(string name) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is JsonObject value
            ? (JsonObject)value.DeepClone()
            : throw new ToolArgumentException("tool.missing-argument", $"Argument '{name}' is required and must be an object.");

    internal string IntentJson(params string[] fields)
    {
        var intent = new JsonObject();
        foreach (var field in fields)
            if (_arguments.TryGetPropertyValue(field, out var value)) intent[field] = value?.DeepClone();
        return intent.ToJsonString();
    }

    public bool Flag(string name) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

    public int Int(string name, int fallback) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is JsonValue value && value.TryGetValue<int>(out var number)
            ? number : fallback;

    public IReadOnlyList<string> List(string name) =>
        _arguments.TryGetPropertyValue(name, out var node) && node is JsonArray array
            ? array.Select(item => item?.GetValue<string>() ?? string.Empty).Where(text => text.Length > 0).ToList() : [];

    public IReadOnlyList<string> RequiredList(string name)
    {
        if (!_arguments.TryGetPropertyValue(name, out var node) || node is not JsonArray array)
            throw new ToolArgumentException("tool.missing-argument", $"Argument '{name}' is required and must be an array of strings.");
        var values = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                values.Add(text);
            else
                throw new ToolArgumentException("tool.invalid-argument", $"Every item in '{name}' must be a nonempty string.");
        }
        return values;
    }
}

/// <summary>A tool call whose arguments cannot be used; reported as a refusal the model can fix.</summary>
internal sealed class ToolArgumentException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>One thing the model can call: its text, input schema, hints, and how it runs.</summary>
/// <param name="Name">The built-in tool name; a profile may show the model another.</param>
/// <param name="Description">Written as onboarding for a model: when to call it, what comes back, what to do next.</param>
/// <param name="Class">The class of every catalogued command this tool is built over, or the tool's own.</param>
/// <param name="InputSchema">JSON Schema for the arguments, with the project already bound by the server.</param>
/// <param name="ReadOnly">The MCP <c>readOnlyHint</c>: the call changes nothing in Motif's store or the project.</param>
/// <param name="Idempotent">The MCP <c>idempotentHint</c>: repeating the call with the same arguments adds nothing.</param>
/// <param name="OnByDefault">Whether the built-in profile exposes the tool.</param>
/// <param name="HasDetail">Whether the tool honours <c>detail</c> and <c>limit</c>.</param>
/// <param name="Serialized">Whether the call opens the project and must take its turn.</param>
/// <param name="Run">Runs the call; it returns refusals rather than throwing for anything the command declines.</param>
internal sealed record AgentTool(
    string Name,
    string Description,
    AgentClass Class,
    JsonObject InputSchema,
    bool ReadOnly,
    bool Idempotent,
    bool OnByDefault,
    bool HasDetail,
    bool Serialized,
    Func<ServerContext, ToolArgs, CancellationToken, Task<ToolOutcome>> Run)
{
    /// <summary>The argument names the schema declares.</summary>
    public IEnumerable<string> ArgumentNames => ((JsonObject)InputSchema["properties"]!).Select(pair => pair.Key);
}
