using System.Diagnostics;
using SIL.Motif.Mcp.Exposure;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
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
/// <param name="ParserPath">The parser the server's jobs use, or <see langword="null"/> to locate one.</param>
/// <param name="Environment">
/// The environment variables the server's settings read (worker root, test timeouts), or <see langword="null"/>
/// for the process's own. An in-process host gives its server its own without touching the process.
/// </param>
public sealed record McpLaunchOptions(
    string? Profile = null, string? ActivityLogPath = null, IJobRunnerLauncher? Runner = null,
    bool AdvancedAiModeEnabled = false, string? ParserPath = null,
    IReadOnlyDictionary<string, string?>? Environment = null);

/// <summary>
/// The MCP server over Motif's command catalog: it lists the tools a profile selects and runs each call
/// in-process through the same handlers the CLI and the window use. A refusal becomes a tool error the model
/// can act on. Only the protocol uses standard output; everything else goes to the log writer.
/// </summary>
public static class MotifMcpServer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string AdvancedAiSetupPromptName = "motif_advanced_ai_setup";
    private const string AdvancedAiSetupPromptDescription =
        "Explain how to enable Motif tools after Advanced AI mode was off at server startup.";

    /// <summary>Serves one client over <paramref name="input"/> and <paramref name="output"/> until it disconnects.</summary>
    public static async Task RunAsync(McpLaunchOptions options, Stream input, Stream output, TextWriter log,
        CancellationToken cancellation)
    {
        using var ambient = options.Environment is null ? null : AmbientEnvironment.Push(options.Environment);
        var profile = options.Profile is null ? ToolProfile.Builtin : ToolProfile.Load(options.Profile);
        var context = new ServerContext(string.Empty, MotifProductVersion.CurrentText,
            options.Runner ?? ProcessRunnerLauncher.FromEnvironment(), new ActivityLog(options.ActivityLogPath), profile, log, options.AdvancedAiModeEnabled, options.ParserPath);
        var policy = ExposurePolicy.Create(new ExposureSettings(
            new SIL.Motif.Commands.Catalog.CommandSurfacePolicy(false, options.AdvancedAiModeEnabled), profile), AgentTools.All);
        var exposed = policy.Tools;
        var includeParsimonyResources = policy.Exposes("parsimony measures");
        var prompts = options.AdvancedAiModeEnabled
            ? Array.Empty<Prompt>()
            : new[]
            {
                new Prompt
                {
                    Name = AdvancedAiSetupPromptName,
                    Title = "Enable Motif tools",
                    Description = AdvancedAiSetupPromptDescription,
                    Arguments = [],
                },
            };
        var setupPromptText =
            SIL.Motif.Commands.Preferences.FileAdvancedAiModePreferenceStore.DisabledServerInstructions +
            " Check the current mode with 'motif settings advanced-ai'. This prompt only gives setup instructions; " +
            "it does not read or change any project or setting.";
        using var loggerFactory = new StderrLoggerFactory(log);
        var server = McpServer.Create(new StreamServerTransport(input, output, "motif"), new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "motif", Version = MotifProductVersion.CurrentText },
            ServerInstructions = policy.Instructions,
            Capabilities = new ServerCapabilities
            {
                Tools = new ToolsCapability(),
                Resources = new ResourcesCapability(),
                Prompts = new PromptsCapability(),
            },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult
                {
                    Tools = exposed.Select(tool => tool.Describe()).ToList(),
                }),
                CallToolHandler = async (request, token) => await CallAsync(
                    context, exposed, request.Params?.Name ?? string.Empty, request.Params?.Arguments, token),
                ListPromptsHandler = (_, _) => ValueTask.FromResult(new ListPromptsResult
                {
                    Prompts = prompts,
                }),
                GetPromptHandler = (request, _) =>
                {
                    if (options.AdvancedAiModeEnabled || request.Params?.Name != AdvancedAiSetupPromptName)
                        throw new McpProtocolException("The requested prompt is not available.", McpErrorCode.InvalidParams);
                    return ValueTask.FromResult(new GetPromptResult
                    {
                        Description = AdvancedAiSetupPromptDescription,
                        Messages =
                        [
                            new PromptMessage
                            {
                                Role = Role.User,
                                Content = new TextContentBlock { Text = setupPromptText },
                            },
                        ],
                    });
                },
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
    internal static IReadOnlyList<ExposedTool> Expose(ToolProfile profile) =>
        ExposurePolicy.Create(new ExposureSettings(
            new SIL.Motif.Commands.Catalog.CommandSurfacePolicy(false, true), profile), AgentTools.All).Tools;

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
        if (ExposurePolicy.CallGate(context.AdvancedAiModeEnabled) is { } gate)
        {
            outcome = gate;
            result = RefusalMapper.ToResult(outcome.Refusal!, tools, args);
        }
        else if (tool is null)
        {
            outcome = ToolOutcome.Refused("tool.unknown", FailureReason.InvalidArgument,
                $"There is no tool named '{name}'. The tools are: {string.Join(", ", tools.Select(t => t.Name))}.");
            result = RefusalMapper.ToResult(outcome.Refusal!, tools, args);
        }
        else
        {
            outcome = await RunAsync(context, tool, args, cancellation);
            result = outcome.Refusal is { } refusal ? RefusalMapper.ToResult(refusal, tools, args) : Success(tool, outcome, args);
        }
        context.Activity.Record(new ActivityEntry(DateTimeOffset.UtcNow, tool?.Tool.Name ?? name, args,
            result.IsError == true, outcome.Refusal?.Code, ResultBytes(result), clock.ElapsedMilliseconds,
            outcome.JobId, context.Profile.Name, outcome.FindComposer,
            tool is null ? [] : ComposerVerbs(tool.Tool, args)),
            outcome.Refusal is { } refused ? RefusalMapper.NextFor(refused, tools, args) : outcome.Next);
        return result;
    }

    /// <summary>The Composer verbs a call addresses, read from the catalog commands the tool serves.</summary>
    private static string[] ComposerVerbs(AgentTool tool, JsonObject args)
    {
        var verbs = new List<string>();
        foreach (var command in CommandCatalog.All.Where(command => tool.Requests.Contains(command.RequestType)))
        {
            if (command.RequestType == typeof(SIL.Motif.Commands.Requests.ComposeBatchRequest))
            {
                if (args["intents"] is JsonArray intents)
                    verbs.AddRange(intents.Select(intent => intent?["verb"] is JsonValue value &&
                        value.TryGetValue<string>(out var verb) ? verb : null).OfType<string>());
            }
            else if (command.Name.StartsWith("compose-", StringComparison.Ordinal) || command.Name == "order-allomorphs")
                verbs.Add(command.Name);
        }
        return verbs.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static async Task<ToolOutcome> RunAsync(ServerContext context, ExposedTool tool, JsonObject args,
        CancellationToken cancellation)
    {
        var gated = tool.Tool.Serialized;
        try
        {
            var toolArgs = new ToolArgs(args, tool.Tool.ArgumentNames);
            ValidateDetailAndLimit(tool, args);
            if (tool.Tool.ArgumentNames.Contains("project"))
                context = context.ForProject(ProjectResolver.Resolve(toolArgs.Required("project"),
                    SIL.Motif.Worker.RunnerOptions.ResolveRoot()));
            if (gated) await context.ProjectGate.WaitAsync(cancellation);
            try { return NoteNotice.Apply(context, args, await tool.Tool.Run(context, toolArgs, cancellation)); }
            finally { if (gated) context.ProjectGate.Release(); }
        }
        catch (ProfileException exception)
        {
            return ToolOutcome.Refused("project.unknown", FailureReason.InvalidArgument, exception.Message);
        }
        catch (SIL.Motif.Projection.ProjectWritingSystemsUnavailableException exception)
        {
            // A read opens a private copy; the remedy must name the project's own folder, not that deleted copy.
            return ToolOutcome.Refused(ProjectStoreCommand.WritingSystemRefusal(exception, context.ProjectPath));
        }
        catch (ToolArgumentException exception)
        {
            return ToolOutcome.Refused(exception.Code, exception.Reason, exception.Message);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return ToolOutcome.Refused("tool.cancelled", FailureReason.Cancelled, "The call was cancelled.");
        }
        catch (Exception exception)
        {
            // Busy, not Refused: an unexpected failure may be transient, and the code's hint caps the retry at one.
            await context.Log.WriteLineAsync($"tool {tool.Name} failed: {exception}");
            return ToolOutcome.Refused("tool.failed", FailureReason.Busy,
                $"The tool failed unexpectedly: {exception.Message}",
                new Dictionary<string, string> { ["toolName"] = tool.Name });
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

    private static void ValidateDetailAndLimit(ExposedTool tool, JsonObject args)
    {
        if (tool.Tool.ArgumentNames.Contains("detail"))
        {
            if (!args.TryGetPropertyValue("detail", out var detailNode))
                args["detail"] = tool.DefaultDetail;
            else
            {
                var choices = (tool.Tool.InputSchema["properties"]?["detail"]?["enum"] as JsonArray)?
                    .Select(choice => choice?.GetValue<string>() ?? string.Empty).ToArray() ?? [];
                if (detailNode is not JsonValue detailValue || !detailValue.TryGetValue<string>(out var detail) ||
                    !choices.Contains(detail, StringComparer.Ordinal))
                    throw new ToolArgumentException("tool.invalid-argument",
                        $"Argument 'detail' must be one of: {string.Join(", ", choices)}.");
            }
        }

        if (tool.Tool.InputSchema["properties"]?["limit"] is JsonObject limitSchema &&
            args.TryGetPropertyValue("limit", out var limitNode))
        {
            var minimum = limitSchema["minimum"]?.GetValue<int>() ?? 1;
            var maximum = limitSchema["maximum"]?.GetValue<int>() ?? 200;
            if (limitNode is not JsonValue limitValue || !limitValue.TryGetValue<int>(out var limit) ||
                limit < minimum || limit > maximum)
                throw new ToolArgumentException("tool.invalid-argument",
                    $"Argument 'limit' must be an integer from {minimum} to {maximum}.");
        }
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
