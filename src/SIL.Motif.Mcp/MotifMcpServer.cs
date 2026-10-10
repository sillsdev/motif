using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.Mcp;

/// <summary>What <c>motif mcp</c> was asked to serve.</summary>
/// <param name="Profile">A profile file or shipped profile name; <see langword="null"/> uses the built-in profile.</param>
/// <param name="ActivityLogPath">A JSON Lines file every tool call is appended to, or <see langword="null"/>.</param>
/// <param name="Runner">
/// What wakes the job runner after a job is queued, or <see langword="null"/> for the one the environment selects.
/// </param>
/// <param name="AdvancedAiModeEnabled">Whether the process has confirmed the user's Advanced AI mode choice.</param>
public sealed record McpLaunchOptions(
    string? Profile = null, string? ActivityLogPath = null, IJobRunnerLauncher? Runner = null,
    bool AdvancedAiModeEnabled = false, string? ParserPath = null);

/// <summary>
/// The MCP server over Motif's command catalog: it lists the tools a profile selects and runs each call
/// in-process through the same handlers the CLI and the window use. A refusal becomes a tool error the model
/// can act on. Only the protocol uses standard output; everything else goes to the log writer.
/// </summary>
public static class MotifMcpServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Serves one client over <paramref name="input"/> and <paramref name="output"/> until it disconnects.</summary>
    public static async Task RunAsync(McpLaunchOptions options, Stream input, Stream output, TextWriter log,
        CancellationToken cancellation)
    {

        var profile = options.Profile is null ? ToolProfile.Builtin : ToolProfile.Load(options.Profile);
        var context = new ServerContext(string.Empty, MotifProductVersion.CurrentText,
            options.Runner ?? ProcessRunnerLauncher.FromEnvironment(), new ActivityLog(options.ActivityLogPath), profile, log, options.AdvancedAiModeEnabled, options.ParserPath);
        var exposed = options.AdvancedAiModeEnabled ? Expose(profile) : [];
        var includeParsimonyResources = exposed.Any(tool => tool.Tool.Name == "motif_parsimony_measures");
        using var loggerFactory = new StderrLoggerFactory(log);
        var server = McpServer.Create(new StreamServerTransport(input, output, "motif"), new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "motif", Version = MotifProductVersion.CurrentText },
            ServerInstructions = options.AdvancedAiModeEnabled ? profile.Instructions :
                SIL.Motif.Commands.Preferences.FileAdvancedAiModePreferenceStore.EnableInstruction,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability(),
                Resources = new ResourcesCapability(),
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = exposed.Select(tool => tool.Describe()).ToList(),
                }),
                CallToolHandler = async (request, token) => await CallAsync(
                    context, exposed, request.Params?.Name ?? string.Empty, request.Params?.Arguments, token),
                ListResourcesHandler = (_, _) => ValueTask.FromResult(
                    ParsimonyTools.ListResources(includeParsimonyResources)),
                ReadResourceHandler = (request, _) => ValueTask.FromResult(
                    includeParsimonyResources && request.Params?.Uri == ParsimonyTools.MeasuresResourceUri
                        ? ParsimonyTools.ReadMeasuresResource(string.Empty, MotifProductVersion.CurrentText)
                        : EncodingGuides.ReadResource(request.Params?.Uri ?? string.Empty)),
            },
        }, loggerFactory);
        await using (server)
            await server.RunAsync(cancellation);
    }

    /// <summary>The tools <paramref name="profile"/> exposes, in a stable order.</summary>
    internal static IReadOnlyList<ExposedTool> Expose(ToolProfile profile)
    {
        var known = AgentTools.All.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        foreach (var selection in profile.Tools ?? [])
            if (!known.ContainsKey(selection.Tool))
                throw new ProfileException($"Profile '{profile.Name}' names tool '{selection.Tool}', which does not exist. " +
                    $"Tools are: {string.Join(", ", known.Keys.Order(StringComparer.Ordinal))}.");
        var exposed = new List<ExposedTool>();
        foreach (var tool in AgentTools.All)
            if (profile.SelectionFor(tool.Name, tool.OnByDefault) is { } selection)
                exposed.Add(new ExposedTool(tool, selection.As ?? tool.Name, selection.Description ?? tool.Description,
                    selection.Detail ?? profile.DefaultDetail));
        var duplicate = exposed.GroupBy(tool => tool.Name, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ProfileException($"Profile '{profile.Name}' gives two tools the name '{duplicate.Key}'.");
        return exposed;
    }

    internal static async ValueTask<CallToolResult> CallAsync(ServerContext context, IReadOnlyList<ExposedTool> tools,
        string name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var args = new JsonObject();
        foreach (var (key, value) in arguments ?? new Dictionary<string, JsonElement>())
            args[key] = JsonNode.Parse(value.GetRawText());
        var tool = tools.FirstOrDefault(candidate => candidate.Name == name);
        ToolOutcome outcome;
        CallToolResult result;
        if (!context.AdvancedAiModeEnabled)
        {
            outcome = ToolOutcome.Refused("advanced-ai.disabled", FailureReason.Refused,
                SIL.Motif.Commands.Preferences.FileAdvancedAiModePreferenceStore.EnableInstruction);
            result = RefusalMapper.ToResult(outcome.Refusal!);
        }
        else if (tool is null)
        {
            outcome = ToolOutcome.Refused("tool.unknown", FailureReason.InvalidArgument,
                $"There is no tool named '{name}'. The tools are: {string.Join(", ", tools.Select(t => t.Name))}.");
            result = RefusalMapper.ToResult(outcome.Refusal!);
        }
        else
        {
            outcome = await RunAsync(context, tool, args, cancellation);
            result = outcome.Refusal is { } refusal ? RefusalMapper.ToResult(refusal) : Success(tool, outcome, args);
        }
        context.Activity.Record(new ActivityEntry(DateTimeOffset.UtcNow, tool?.Tool.Name ?? name, args,
            result.IsError == true, outcome.Refusal?.Code, ResultBytes(result), clock.ElapsedMilliseconds,
            outcome.JobId, context.Profile.Name));
        return result;
    }

    private static async Task<ToolOutcome> RunAsync(ServerContext context, ExposedTool tool, JsonObject args,
        CancellationToken cancellation)
    {
        var gated = tool.Tool.Serialized;
        try
        {
            if (tool.Tool.ArgumentNames.Contains("detail") && args["detail"] is null)
                args["detail"] = tool.DefaultDetail;
            var toolArgs = new ToolArgs(args, tool.Tool.ArgumentNames);
            if (tool.Tool.ArgumentNames.Contains("project"))
                context = context.ForProject(ProjectResolver.Resolve(toolArgs.Required("project"),
                    SIL.Motif.Worker.RunnerOptions.ResolveRoot()));
            if (gated) await context.ProjectGate.WaitAsync(cancellation);
            try { return await tool.Tool.Run(context, toolArgs, cancellation); }
            finally { if (gated) context.ProjectGate.Release(); }
        }
        catch (ProfileException exception)
        {
            return ToolOutcome.Refused("project.unknown", FailureReason.InvalidArgument, exception.Message);
        }
        catch (ToolArgumentException exception)
        {
            return ToolOutcome.Refused(exception.Code, FailureReason.InvalidArgument, exception.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return ToolOutcome.Refused("tool.cancelled", FailureReason.Cancelled, "The call was cancelled.");
        }
        catch (Exception exception)
        {
            await context.Log.WriteLineAsync($"tool {tool.Name} failed: {exception}");
            return ToolOutcome.Refused("tool.failed", FailureReason.StoreInconsistent,
                $"The tool failed unexpectedly: {exception.Message}");
        }
    }

    private static CallToolResult Success(ExposedTool tool, ToolOutcome outcome, JsonObject args)
    {
        var detailed = (args["detail"]?.GetValue<string>() ?? tool.DefaultDetail) == "detailed";
        var limit = args["limit"]?.GetValue<int>() ?? 25;
        var value = tool.Tool.HasDetail && !detailed ? ResultShaper.Concise(outcome.Value, limit) : outcome.Value;
        var envelope = new JsonObject { ["ok"] = true, ["result"] = value?.DeepClone() };
        if (outcome.Next is not null) envelope["next"] = outcome.Next;
        var text = envelope.ToJsonString(Json);
        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = outcome.Next is null ? text : text + "\nNext: " + outcome.Next }],
            StructuredContent = JsonSerializer.SerializeToElement(envelope),
        };
    }

    private static int ResultBytes(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Sum(block => System.Text.Encoding.UTF8.GetByteCount(block.Text));
}

/// <summary>A tool as one profile shows it: the model's name and description, and its default detail level.</summary>
internal sealed record ExposedTool(AgentTool Tool, string Name, string Description, string DefaultDetail)
{
    /// <summary>The MCP listing entry, including the hints a client may use to decide what to confirm.</summary>
    public Tool Describe() => new()
    {
        Name = Name,
        Description = Description,
        InputSchema = JsonSerializer.SerializeToElement(Tool.InputSchema),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = Tool.ReadOnly,
            DestructiveHint = false,
            IdempotentHint = Tool.Idempotent,
            OpenWorldHint = false,
        },
    };
}
