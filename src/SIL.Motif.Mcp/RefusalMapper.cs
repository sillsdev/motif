using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Mcp;

/// <summary>
/// Turns a typed <see cref="Refusal"/> into an MCP tool error a model can act on: the code, the sentence, the
/// facts the refusal was computed from, and a <c>Next:</c> line saying what to do instead. A tool error is a
/// result the model reads and recovers from, not a protocol failure.
/// </summary>
internal static class RefusalMapper
{
    /// <summary>What to do after specific refusals; anything not listed falls back to its reason's advice.</summary>
    private static readonly IReadOnlyDictionary<string, string> Hints = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [RefusalCodes.BaselineMissing] = "Call motif_capture_baseline once, then repeat this call.",
        [HostRefusalCodes.BaselineUnavailable] = "Call motif_capture_baseline to take a fresh saved copy, then repeat this call.",
        [RefusalCodes.ProjectBusy] = "Another Motif task holds this project. Wait about ten seconds and repeat the same call.",
        [RefusalCodes.ProjectInUse] = "FieldWorks has the project open. Ask the linguist to save and close it, then repeat.",
        [HostRefusalCodes.ProjectRegistrationFailed] = "Check that the worker root at {workerRoot} and machine store at " +
            "{machineStorePath} are writable and valid. If another writer holds the store, let it finish, " +
            "then call motif_open again.",
        [RefusalCodes.ChangeProjectSaving] = "FieldWorks is saving. Wait a few seconds and repeat the same call.",
        [RefusalCodes.ProjectNotFound] = "The project file is missing. Tell the linguist; do not guess another path.",
        [RefusalCodes.JobWaitTimeout] = "The job is still running. Call this tool again with job set to {jobId} to keep waiting.",
        [RefusalCodes.JobWaitCancelled] = "The wait was cancelled. Call this tool again with job set to {jobId}.",
        [RefusalCodes.ProposalNotFound] = "Call motif_proposals to list the proposals that exist, then use one of those ids.",
        [RefusalCodes.ProposalInvalidId] = "Use the proposalId a tool returned, or one from motif_proposals; do not type one.",
        [RefusalCodes.DraftRevisionConflict] = "The draft changed since you last read it. Call motif_proposals with its id and retry.",
        [RefusalCodes.JobDryRunIncomplete] = "The Dry Run did not complete. Revise the Draft and run a new Dry Run.",
        [RefusalCodes.StoreOtherVersion] = "Motif's data for this project was made by another version. Tell the linguist; do not retry.",
        [RefusalCodes.ParseAlreadyRunning] = "A parse is already running. Wait a minute, then repeat the call.",
        ["assess.parser-memory-limit"] =
            "The parser used more memory than Motif allows. Tell the linguist; do not retry unchanged.",
        [RefusalCodes.AssessParserUnavailable] = "The parser is not installed here. Tell the linguist; evaluation tools cannot run.",
        [RefusalCodes.GrammarCheckParserUnavailable] = "The parser is not installed here. Tell the linguist; evaluation tools cannot run.",
        [RefusalCodes.ProjectWritingSystemMissing] = "This is a setup problem, not damaged data. Tell the linguist what the " +
            "message says to fix; do not retry unchanged.",
        [HostRefusalCodes.ToolUnknownArgument] = "Remove the argument the message names, or check this tool's input schema.",
        [HostRefusalCodes.ToolMissingArgument] = "Add the argument the message names, then call again.",
        [HostRefusalCodes.ToolFailed] = "This failed unexpectedly; the diagnostic log has 'tool {toolName} failed'. Repeat the call once; " +
            "if it fails again, stop and tell the linguist.",
    };

    private static readonly IReadOnlyDictionary<FailureReason, string> ReasonHints = new Dictionary<FailureReason, string>
    {
        [FailureReason.InvalidArgument] = "Fix the argument the message names and call again.",
        [FailureReason.NotFound] = "Check the id or name against what the read tools return, then call again.",
        [FailureReason.Refused] = "Do not repeat this call unchanged. Read the message and change what it names.",
        [FailureReason.Cancelled] = "The work was cancelled. Call again if it is still wanted.",
        [FailureReason.Busy] = "Motif is busy. Wait a few seconds and repeat the same call.",
        [FailureReason.StoreInconsistent] = "Motif's stored data for this project is damaged. Stop and tell the linguist.",
    };

    /// <summary>The <c>Next:</c> advice for a refusal, with <c>{fact}</c> placeholders filled from its facts.</summary>
    public static string NextFor(Refusal refusal)
    {
        var hint = Hints.TryGetValue(refusal.Code, out var specific) ? specific : ReasonHints[refusal.Reason];
        foreach (var (key, value) in refusal.Facts) hint = hint.Replace("{" + key + "}", value, StringComparison.Ordinal);
        return hint;
    }

    /// <summary>The error text a model reads: code and sentence, then facts, then the <c>Next:</c> line.</summary>
    public static string Text(Refusal refusal)
    {
        var text = new StringBuilder().Append(refusal.Code).Append(": ").Append(refusal.Message);
        foreach (var (key, value) in refusal.Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            text.Append('\n').Append(key).Append(": ").Append(value);
        return text.Append("\nNext: ").Append(NextFor(refusal)).ToString();
    }

    public static CallToolResult ToResult(Refusal refusal) => new()
    {
        IsError = true,
        Content = [new TextContentBlock { Text = Text(refusal) }],
        StructuredContent = JsonSerializer.SerializeToElement(new JsonObject
        {
            ["ok"] = false,
            ["code"] = refusal.Code,
            ["reason"] = refusal.Reason.ToString(),
            ["message"] = refusal.Message,
            ["facts"] = new JsonObject(refusal.Facts.Select(pair =>
                new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value))),
            ["next"] = NextFor(refusal),
        }),
    };
}
