using System.Text.Json.Nodes;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>What one tool call produced: a value and what to do next, or a typed refusal.</summary>
internal sealed record ToolOutcome(JsonNode? Value, Refusal? Refusal, string? Next, string? JobId = null,
    JsonObject? FindComposer = null)
{
    public static ToolOutcome Ok(JsonNode value, string? next = null, string? jobId = null) =>
        new(value, null, next, jobId);

    public static ToolOutcome Refused(Refusal refusal) => new(null, refusal, null);

    public static ToolOutcome Refused(string code, FailureReason reason, string message,
        IReadOnlyDictionary<string, string>? facts = null) => Refused(new Refusal(code, reason, message, facts));

    /// <summary>Converts a command's typed outcome using the same JSON the CLI prints with <c>--json</c>.</summary>
    public static ToolOutcome From<T>(CommandOutcome<T> outcome, Func<T, string?>? next = null) where T : class =>
        outcome.Succeeded
            ? Ok(JsonNode.Parse(ProjectionJson.Serialize(outcome.Value!))!, ComposeHint(outcome.Value!, next?.Invoke(outcome.Value!)))
            : Refused(outcome.Refusal!);

    private static string? ComposeHint<T>(T value, string? next) where T : class =>
        value is ComposedOperationsResponse composed &&
        (next is null || !next.StartsWith("Draft now has ", StringComparison.Ordinal))
            ? $"Draft now has {composed.OperationCount} operations." + (next is null ? "" : $" {next}")
            : next;

    public static ToolOutcome From(CommandOutcome<JsonObject> outcome, string? next = null) =>
        outcome.Succeeded ? Ok(outcome.Value!, next) : Refused(outcome.Refusal!);
}
