using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using SIL.Motif.Commands;
using SIL.Motif.Worker;

namespace SIL.Motif.Mcp;

/// <summary>
/// Tells an assistant, on every result about a project, that the linguist has open Requests: one text line and
/// <c>openRequestsCount</c> in the structured content. The line carries the count and the tool to call, never a Request's
/// words, so a note cannot reach the assistant by this path. It is added after the result is chosen, so a refusal carries
/// it too.
/// </summary>
internal static class OpenRequestsNotice
{
    /// <summary>The line an assistant reads when <paramref name="count"/> Requests are open.</summary>
    public static string Line(int count) => count == 1
        ? "You have 1 open Motif Request. Call motif_list_requests to review it."
        : $"You have {count} open Motif Requests. Call motif_list_requests to review them.";

    public static CallToolResult Apply(ServerContext context, JsonObject args, CallToolResult result)
    {
        var project = ProjectOf(context, args);
        if (project is null) return result;
        var count = RequestCommands.OpenCount(project, context.ProductVersion);
        if (count == 0) return result;
        if (result.StructuredContent is { } structured &&
            JsonNode.Parse(structured.GetRawText()) is JsonObject envelope)
        {
            envelope["openRequestsCount"] = count;
            result.StructuredContent = JsonSerializer.SerializeToElement(envelope);
        }
        result.Content = [.. result.Content, new TextContentBlock { Text = Line(count) }];
        return result;
    }

    // The project is the call's own argument; motif_open names it as a path, and it never opens a new store here.
    private static string? ProjectOf(ServerContext context, JsonObject args)
    {
        try
        {
            if (Text(args, "project") is { } project) return ProjectResolver.Resolve(project, RunnerOptions.ResolveRoot());
            if (Text(args, "path") is { } path && path.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ProfileException or ArgumentException or NotSupportedException or
            PathTooLongException) { return null; }
        return string.IsNullOrEmpty(context.ProjectPath) ? null : context.ProjectPath;
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text : null;

    /// <summary>Shows every note in a Request list as the linguist's quoted words.</summary>
    public static ToolOutcome QuoteNotes(ToolOutcome outcome)
    {
        if (outcome.Value is not JsonObject value || value["requests"] is not JsonArray requests) return outcome;
        foreach (var request in requests.OfType<JsonObject>())
            if (request["note"] is JsonValue note && note.TryGetValue<string>(out var text))
                request["note"] = RequestCommands.QuoteNote(text);
        foreach (var request in requests.OfType<JsonObject>())
            if (request["targetLabel"] is JsonValue label && label.TryGetValue<string>(out var words))
                request["targetLabel"] = RequestCommands.QuoteWords(words);
        foreach (var request in requests.OfType<JsonObject>())
            if (request["targetId"] is JsonValue id && id.TryGetValue<string>(out var target))
                request["targetId"] = RequestCommands.QuoteWords(target);
        return outcome;
    }
}
