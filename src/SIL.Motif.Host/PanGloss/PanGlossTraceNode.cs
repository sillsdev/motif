namespace SIL.Motif.Host.PanGloss;

/// <summary>
/// One node of a PanGloss derivation trace, exactly as <c>pangloss parse --trace --trace-format json</c>
/// wrote it (PanGloss's <c>trace_render::render_json</c>). Kept verbatim: deciding which branch mattered is
/// the judgement a Handoff reader makes, not Motif.
/// </summary>
/// <param name="Type">The trace node kind — a stratum, template, rule, lookup, or leaf outcome.</param>
/// <param name="Source">The named rule, stratum, or template that produced this node, absent for the
/// language-level root and for leaf outcome nodes.</param>
/// <param name="Subrule">Which subrule or allomorph fired, absent where a rule was not tried.</param>
/// <param name="FailureReason">Why this node did not apply or succeed, absent when it did.</param>
/// <param name="OutputShape">The word shape this node produced, when it produced one.</param>
/// <param name="InputShape">The word shape this node consumed, when it consumed one.</param>
/// <param name="Children">This node's children, in the order PanGloss traced them.</param>
public sealed record PanGlossTraceNode(
    string Type,
    string? Source,
    int? Subrule,
    string? FailureReason,
    string? OutputShape,
    string? InputShape,
    IReadOnlyList<PanGlossTraceNode> Children)
{
    public string? OutcomeStatus { get; init; }
    public string? OutcomeEventType { get; init; }
    public string? FailureContext { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
    public IReadOnlyList<PanGlossTraceMorph> AttemptedMorphs { get; init; } = [];
}

/// <summary>Producer context attached to a v2 trace node when available.</summary>
public static class PanGlossTraceNodeContext
{
    public const string Unavailable = "unavailable";
}
