using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a synchronous <c>assess</c> run produced (design decision 4): the Baseline it measured, the
/// Selection it composed, every Assessment id it recorded, and PanGloss's own statistics summary.
/// </summary>
public sealed record AssessCommandResponse(
    BaselineCaptureResponse Baseline,
    SelectionProjection Selection,
    IReadOnlyList<string> AssessmentIds,
    string SummaryMarkdown)
{
    public IReadOnlyList<AssessmentWordResult> Words { get; init; } = new AssessmentWordResult[0];
    public IReadOnlyList<ProducedAssessmentReference> Measurements { get; init; } = new ProducedAssessmentReference[0];
    public string CompletionSummary { get; init; } = string.Empty;
    public string CorrectnessStatus { get; init; } = "Correctness unavailable: authoritative analysis identities are not supplied.";
    public IReadOnlyList<string>? GrammarWarnings { get; init; }
}

/// <summary>One word's completion is independent of whatever findings the parser returned.</summary>
public sealed record AssessmentWordResult(
    string Word, string Outcome, bool IsIncomplete, string CompletionStatus, int? ElapsedMs, string? RawSignature)
{
    public ParseWordEvidence? Morphology { get; init; }
    public WordCorrectness? Correctness { get; init; }
    public bool HasUnavailableEvidence => Morphology?.Unavailable is { Count: > 0 };
    public string EvidenceStatus => Morphology switch
    {
        null => "Morphology evidence unavailable.",
        { InvalidShape: true } => "Morphology evidence unavailable: invalid shape.",
        { Capped: true } or { TimedOut: true } => "Partial morphology evidence; search incomplete.",
        { Unavailable.Count: > 0 } => "Some morphology evidence is unavailable.",
        { Analyses.Count: 0 } => "No parser readings were returned.",
        { Analyses.Count: 1 } => "1 parser reading.",
        { Analyses.Count: var count } => $"{count} parser readings.",
    };
    public string CorrectnessStatus => Correctness == null ? "Correctness unavailable"
        : Correctness.Matched + "/" + Correctness.Expected + " approved readings matched; " + Correctness.Status +
          (Correctness.Unavailable.Count == 0 ? string.Empty : "; comparison unavailable: " + string.Join("; ", Correctness.Unavailable));
}

/// <summary>The exact measurement recorded by this call, with its kind and shared invocation identity.</summary>
public sealed record ProducedAssessmentReference(string AssessmentId, string Kind, string? InvocationId);
