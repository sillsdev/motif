using System.Text.Json.Nodes;
using SIL.Motif.Commands;

namespace SIL.Motif.Mcp;

/// <summary>
/// Tells an agent that the person left a note. Every result about one Proposal carries <c>noteChanged</c>, true when
/// the person wrote after the Rationale was last written; the next step then says how to read the note. It needs no
/// reader identity, because the agent's own next Rationale write clears it.
/// </summary>
internal static class NoteNotice
{
    public static ToolOutcome Apply(ServerContext context, JsonObject args, ToolOutcome outcome)
    {
        if (outcome.Refusal is not null || outcome.Value is not JsonObject value ||
            string.IsNullOrEmpty(context.ProjectPath)) return outcome;
        var selector = Text(args, "proposal") ?? Text(args, "draft") ?? Text(value, "proposalId") ??
            Text(value, "proposal");
        if (selector is null) return outcome;
        if (ProposalTexts.NoteChangedFor(context.ProjectPath, context.ProductVersion, selector) is not { } found)
            return outcome;
        value["noteChanged"] = found.NoteChanged;
        if (!found.NoteChanged) return outcome;
        var advice = $"The person left a note on this Proposal. Read it with motif_proposals(proposal='{found.ProposalId}'): " +
            "the note field. Answer it in a new Rationale with motif_set_rationale.";
        return outcome with { Next = outcome.Next is null ? advice : outcome.Next + " " + advice };
    }

    private static string? Text(JsonObject node, string name) =>
        node[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text : null;
}
