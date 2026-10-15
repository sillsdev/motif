using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Help;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// Turns a typed <see cref="Refusal"/> into an MCP tool error a model can act on: the code, the sentence, the
/// facts the refusal was computed from, and a <c>Next:</c> line saying what to do instead. A tool error is a
/// result the model reads and recovers from, not a protocol failure.
/// </summary>
internal static class RefusalMapper
{
    private static readonly HelpCatalog Help = HelpCatalog.Load();
    private static readonly Lazy<IReadOnlyList<ExposedTool>> DefaultTools = new(() => MotifMcpServer.Expose(ToolProfile.Builtin));

    private static readonly IReadOnlyDictionary<FailureReason, string> ReasonHints = new Dictionary<FailureReason, string>
    {
        [FailureReason.InvalidArgument] = "Fix the argument the message names and call again.",
        [FailureReason.NotFound] = "Check the id or name against what the read tools return, then call again.",
        [FailureReason.Refused] = "Do not repeat this call unchanged. Read the message and change what it names.",
        [FailureReason.Cancelled] = "The work was cancelled. Call again if it is still wanted.",
        [FailureReason.Busy] = "Motif is busy. Wait a few seconds and repeat the same call.",
        [FailureReason.StoreInconsistent] = "Motif's stored data for this project is damaged. Stop and tell the linguist.",
    };

    /// <summary>
    /// The <c>Next:</c> advice for a refusal: the registry's Help sentence naming what is missing, then the next
    /// step rendered for <paramref name="exposed"/>, with <c>{fact}</c> placeholders filled from the refusal's facts.
    /// </summary>
    /// <param name="refusal">The refusal to advise on.</param>
    /// <param name="exposed">The tools the active profile exposes; the built-in profile's when omitted.</param>
    /// <param name="callerArgs">The arguments of the refused call, which a next step may reuse.</param>
    public static string NextFor(Refusal refusal, IReadOnlyList<ExposedTool>? exposed = null, JsonObject? callerArgs = null)
    {
        var hint = RefusalCatalog.Specs.GetValueOrDefault(refusal.Code)?.Next is { } step
            && Help.RefusalWhy(refusal.Code) is { } why
                ? why + RenderStep(step, refusal, exposed ?? DefaultTools.Value, callerArgs)
                : ReasonHints[refusal.Reason];
        foreach (var (key, value) in refusal.Facts) hint = hint.Replace("{" + key + "}", value, StringComparison.Ordinal);
        return hint;
    }

    // A command with no tool in the active profile is the person's to run, never a bare CLI line for the agent.
    private static string RenderStep(NextStep step, Refusal refusal, IReadOnlyList<ExposedTool> exposed, JsonObject? callerArgs)
    {
        if (step.Command == NextStep.AskPerson) return string.Empty;
        var caller = (callerArgs ?? []).Where(pair => pair.Value is JsonValue value && value.TryGetValue<string>(out _))
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);
        var values = step.Args.Select(pair => (pair.Key, Value: pair.Value.Resolve(refusal.Facts, caller))).ToList();
        if (step.Command == NextStep.RepeatCall)
            return " Repeat this call" + SetClauses(values) + ".";
        var request = CommandCatalog.All.Single(command => command.Name == step.Command).RequestType;
        if (exposed.FirstOrDefault(tool => tool.Tool.Requests.Contains(request)) is { } tool)
            return " Call `" + tool.Name + "`" + SetClauses(values) + ".";
        var flags = string.Concat(values.Select(pair => " --" + pair.Key.Replace('_', '-') + " " + (pair.Value ?? "<" + pair.Key + ">")));
        return " Ask the person to run `motif " + step.Command + flags + "`.";
    }

    private static string SetClauses(IReadOnlyList<(string Name, string? Value)> values)
    {
        var given = values.Where(pair => pair.Value is not null).Select(pair => pair.Name + " set to " + pair.Value).ToList();
        return given.Count == 0 ? string.Empty : " with " + string.Join(" and ", given);
    }

    /// <summary>The error text a model reads: code and sentence, then facts, then the <c>Next:</c> line.</summary>
    public static string Text(Refusal refusal, IReadOnlyList<ExposedTool>? exposed = null, JsonObject? callerArgs = null)
    {
        var text = new StringBuilder().Append(refusal.Code).Append(": ").Append(refusal.Message);
        foreach (var (key, value) in refusal.Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append('\n').Append(key).Append(": ").Append(value);
        return text.Append("\nNext: ").Append(NextFor(refusal, exposed, callerArgs)).ToString();
    }

    public static CallToolResult ToResult(Refusal refusal, IReadOnlyList<ExposedTool>? exposed = null,
        JsonObject? callerArgs = null) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = Text(refusal, exposed, callerArgs) }],
        StructuredContent = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["ok"] = false,
            ["code"] = refusal.Code,
            ["reason"] = refusal.Reason.ToString(),
            ["message"] = refusal.Message,
            ["facts"] = new JsonObject(refusal.Facts.Select(pair =>
                new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))),
            ["next"] = NextFor(refusal, exposed, callerArgs),
        }),
    };
}
