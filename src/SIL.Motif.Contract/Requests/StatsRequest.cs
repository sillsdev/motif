using System.Collections.Generic;

namespace SIL.Motif.Contract.Requests;

/// <summary>Whether a statistics query renders PanGloss's own text view or its rows (design decision 5).</summary>
public enum StatsOutputKind
{
    /// <summary>PanGloss's own text summary, forwarded verbatim.</summary>
    Text,

    /// <summary>PanGloss's rows, requested as JSONL and parsed into typed application data.</summary>
    JsonRows,
}

/// <summary>
/// Which project to query, which Assessment to query it against, and what the caller's remaining
/// arguments after <c>--</c> asked PanGloss's own <c>stats</c> vocabulary for (design decision 5).
/// </summary>
/// <param name="ProjectPath">The project whose recorded Assessment supplies the retained grammar and statistics.</param>
/// <param name="ProposalId">
/// <c>null</c> to query the current Baseline Assessment; a Proposal id to query its Trial Assessment instead.
/// </param>
/// <param name="Output">Whether to render PanGloss's text view or its rows.</param>
/// <param name="ForwardedArguments">
/// Everything the caller wrote after a standalone <c>--</c>, in order and unchanged. Motif never tokenizes,
/// reorders, or otherwise interprets these; they reach PanGloss exactly as supplied.
/// </param>
public sealed record StatsRequest(
    string ProjectPath,
    string? ProposalId,
    StatsOutputKind Output,
    IReadOnlyList<string> ForwardedArguments)
{
    /// <summary>An exact ObjectTiming Assessment id; mutually exclusive with <see cref="ProposalId"/>.</summary>
    public string? AssessmentId { get; init; }
}
