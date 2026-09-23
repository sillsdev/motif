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
    /// <summary>The immutable retained result identity for this successful Assessment.</summary>
    public string InvocationId { get; init; } = string.Empty;
    /// <summary>The complete caller request and resolved Selection provenance for this result.</summary>
    public SelectionDescriptor? SelectionDescriptor { get; init; }
    public IReadOnlyList<AssessmentWordResult> Words { get; init; } = new AssessmentWordResult[0];
    public IReadOnlyList<ProducedAssessmentReference> Measurements { get; init; } = new ProducedAssessmentReference[0];
    public string CompletionSummary { get; init; } = string.Empty;
    public string CorrectnessStatus { get; init; } = "Correctness unavailable: authoritative analysis identities are not supplied.";
    public IReadOnlyList<string>? GrammarWarnings { get; init; }
    /// <summary><see cref="GrammarWarnings"/>, line for line, with each named object resolved and linked.</summary>
    public IReadOnlyList<GrammarWarning>? GrammarWarningDetails { get; init; }
}

/// <summary>One word's completion is independent of whatever findings the parser returned.</summary>
public sealed record AssessmentWordResult(
    string Word, string Outcome, bool IsIncomplete, string CompletionStatus, int? ElapsedMs, string? RawSignature)
{
    public ParseWordEvidence? Morphology { get; init; }
    /// <summary><see cref="ParseWordEvidence.Analyses"/>, reading for reading, as forms, glosses and categories.</summary>
    public IReadOnlyList<ParserReading>? Readings { get; init; }
    /// <summary>
    /// A <c>silfw:</c> link selecting this word in FieldWorks' Word Analyses, where Parser ▸ Try a Word opens
    /// with it entered; <see langword="null"/> when the project has no wordform spelled this way.
    /// </summary>
    public string? TryWordLink { get; init; }
    public WordCorrectness? Correctness { get; init; }
    /// <summary>
    /// One grade per entry of <see cref="Readings"/>, in the same order: <c>approved</c> when the reading is an
    /// analysis the project approves, <c>disapproved</c> when it is one the project rejected, <c>candidate</c> when
    /// it is one the project holds without a human verdict, and <c>no-opinion</c> when the project holds nothing
    /// like it. <see langword="null"/> when the project's analyses were not read.
    /// </summary>
    public IReadOnlyList<string>? ReadingGrades { get; init; }
    /// <summary>
    /// What the project held for this word when it was assessed, as one of the <see cref="Responses.ProjectStanding"/>
    /// values; <see langword="null"/> when the project's analyses were not read.
    /// </summary>
    public string? ProjectStanding { get; init; }
    /// <summary>Approved analyses of this word the parser did not produce, as morphs a person reads.</summary>
    public IReadOnlyList<ParserReading>? MissedApproved { get; init; }
    /// <summary>How many rule applications and lexical lookups the parser attempted for this word, when measured.</summary>
    public int? Attempts { get; init; }
    /// <summary>How many of those attempts passed, when measured.</summary>
    public int? Passes { get; init; }
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
public sealed record ProducedAssessmentReference(string AssessmentId, string Kind, string InvocationId);
