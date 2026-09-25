using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What a synchronous <c>assess</c> run produced (design decision 4): the Baseline it measured, the
/// Selection it composed, every Assessment id it recorded, and a summary of the batch statistics.
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
    /// <summary>Stored ParseTime re-runs to apply, in order, over this result's original word timings.</summary>
    public IReadOnlyList<string> TimingOverrideAssessmentIds { get; init; } = Array.Empty<string>();
    public string CompletionSummary { get; init; } = string.Empty;
    public string CorrectnessStatus { get; init; } = "Correctness unavailable: authoritative analysis identities are not supplied.";
    public IReadOnlyList<string>? GrammarWarnings { get; init; }
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
    /// <summary>How many chosen Text occurrences this word represents in the run's resolved Selection.</summary>
    public int? OccurrenceCount { get; init; }
    /// <summary>The command's Fix these first priority, or <see langword="null"/> outside that panel.</summary>
    public FixFirstPriority? FixFirst { get; init; }
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

/// <summary>The one class of word a person should inspect first in the Matrix.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<FixFirstCategory>))]
public enum FixFirstCategory
{
    /// <summary>An approved analysis was not produced.</summary>
    ApprovedNoParse,
    /// <summary>A different reading was produced while an approved analysis was missed.</summary>
    ApprovedNoMatch,
    /// <summary>The parser produced an analysis the project rejected.</summary>
    RejectedRebuilt,
    /// <summary>The parser produced no reading for a candidate.</summary>
    CandidateNoParse,
}

/// <summary>The command's rank and short explanation for one Fix these first row.</summary>
/// <param name="Category">The cell condition this row represents.</param>
/// <param name="Rank">Its position among the four ordered conditions.</param>
/// <param name="Label">The short cell name shown with the word.</param>
/// <param name="Explanation">A single sentence explaining why the word is listed.</param>
public sealed record FixFirstPriority(FixFirstCategory Category, int Rank, string Label, string Explanation);
