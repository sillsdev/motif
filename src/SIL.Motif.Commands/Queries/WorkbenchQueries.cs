using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which project's history to read.</summary>
public sealed record ProjectHistoryRequest(string ProjectPath);

/// <summary>What happened to a project in Motif, newest first.</summary>
public sealed record ProjectHistoryResponse(IReadOnlyList<ProjectHistoryEntry> Entries);

/// <summary>What kind of event a <see cref="ProjectHistoryEntry"/> records.</summary>
public enum ProjectHistoryKind
{
    /// <summary>A Baseline was captured.</summary>
    Baseline,

    /// <summary>An Assessment completed.</summary>
    Assessment,

    /// <summary>A Handoff was written.</summary>
    Handoff,
}

/// <summary>One event in a project's history, in words a person reads at a glance.</summary>
/// <param name="At">When it happened.</param>
/// <param name="Kind">What kind of event it was.</param>
/// <param name="Summary">One line: what was captured, assessed, or written, and how it came out.</param>
public sealed record ProjectHistoryEntry(DateTimeOffset At, ProjectHistoryKind Kind, string Summary);

/// <summary>Which project's grammar to check; the grammar is read from its current Baseline.</summary>
public sealed record GrammarCheckRequest(string ProjectPath);

/// <summary>
/// What the parser reports about a project's grammar as a whole, with no Text or word involved: the warnings
/// it prints while loading the grammar, and its grammar-authoring health findings.
/// </summary>
/// <param name="Findings">
/// Load warnings and health findings together. A health finding's <see cref="GrammarWarning.Severity"/> is
/// <c>error</c> or <c>warning</c>; a load warning's is the parser's own prefix, as for an Assessment.
/// </param>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was no grammar to read.</param>
public sealed record GrammarCheckResponse(IReadOnlyList<GrammarWarning> Findings, bool HasBaseline);

/// <summary>Which Texts to read words from; empty reads none.</summary>
public sealed record TextWordsRequest(string ProjectPath, IReadOnlyList<Guid> TextIds);

/// <summary>
/// The words of the chosen Texts as they stand in the project: each distinct form once, every place it
/// occurs with the analysis chosen there, and the analyses the project approves and disapproves for it.
/// Also the Texts line by line, for reading the words in place.
/// </summary>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was nothing to read.</param>
public sealed record TextWordsResponse(
    IReadOnlyList<TextWord> Words, IReadOnlyList<TextLines> Texts, bool HasBaseline);

/// <summary>One distinct word form in the chosen Texts.</summary>
/// <param name="Form">The form exactly as a Selection would send it to the parser (NFD).</param>
/// <param name="WordformGuid">The project's wordform for this form, or <see langword="null"/> when it has none.</param>
/// <param name="Occurrences">Every place the form occurs in the chosen Texts, in text order.</param>
/// <param name="Approved">The analyses the project approves for this form.</param>
/// <param name="Disapproved">The analyses the project has rejected for this form.</param>
/// <param name="CandidateCount">
/// How many of the wordform's candidate analyses there are: those carrying no human opinion at all, whoever made
/// them, whether FieldWorks' parser, its guesser, or nobody. FieldWorks lists them as "Analysis Candidates" and
/// offers them as guesses; they are neither approved nor rejected.
/// </param>
/// <param name="IncorrectSpelling">Whether FieldWorks marks the wordform's spelling as incorrect.</param>
public sealed record TextWord(
    string Form,
    string? WordformGuid,
    IReadOnlyList<WordOccurrence> Occurrences,
    IReadOnlyList<ProjectAnalysis> Approved,
    IReadOnlyList<ProjectAnalysis> Disapproved,
    int CandidateCount = 0,
    bool IncorrectSpelling = false);

/// <summary>One place a word occurs, and the analysis chosen there.</summary>
/// <param name="TextId">The Text's own GUID.</param>
/// <param name="TextTitle">The Text's title, for display only.</param>
/// <param name="Line">The line's number within its Text, counting from one, as <see cref="TextLine.Number"/>.</param>
/// <param name="Sentence">The whole line's baseline text.</param>
/// <param name="Status">
/// <c>approved</c>, <c>unapproved</c> or <c>unanalysed</c>, as FieldWorks' interlinear status for this occurrence.
/// </param>
/// <param name="Analysis">The analysis chosen at this occurrence, or <see langword="null"/> when there is none.</param>
public sealed record WordOccurrence(
    Guid TextId, string TextTitle, int Line, string Sentence, string Status, ProjectAnalysis? Analysis);

/// <summary>One analysis the project holds, as morphs a person reads, with a key to compare it by.</summary>
/// <param name="Key">
/// Equal for two analyses exactly when FieldWorks would call them the same analysis: same morph count and, per
/// morph, the same form, grammatical info and inflection type. Sense is not part of it (ADR 0027).
/// </param>
/// <param name="Morphs">The morphs in order, with form, gloss, category and a FieldWorks link.</param>
public sealed record ProjectAnalysis(string Key, IReadOnlyList<ParserReadingMorph> Morphs);

/// <summary>One chosen Text, line by line.</summary>
public sealed record TextLines(Guid TextId, string Title, IReadOnlyList<TextLine> Lines);

/// <summary>One line of a Text: its number and its tokens in order.</summary>
public sealed record TextLine(int Number, IReadOnlyList<TextToken> Tokens);

/// <summary>One token of a line: a word, or the punctuation between words.</summary>
/// <param name="Text">The token as it appears in the line.</param>
/// <param name="Form">For a word, its form as in <see cref="TextWord.Form"/>; <see langword="null"/> for punctuation.</param>
/// <param name="Gloss">For a word, the gloss of the analysis chosen here, or <see langword="null"/>.</param>
/// <param name="Status">For a word, as <see cref="WordOccurrence.Status"/>; <see langword="null"/> for punctuation.</param>
public sealed record TextToken(string Text, string? Form, string? Gloss, string? Status)
{
    /// <summary>For a word, the analysis chosen at this occurrence, morph by morph; otherwise <see langword="null"/>.</summary>
    public ProjectAnalysis? Analysis { get; init; }

    /// <summary>For a word, its word-level gloss at this occurrence, or <see langword="null"/> when none was chosen.</summary>
    public string? WordGloss { get; init; }

    /// <summary>For a word, the grammatical category of the analysis chosen here, or <see langword="null"/>.</summary>
    public string? Category { get; init; }

    /// <summary>For a word, a <c>silfw:</c> link selecting its wordform in FieldWorks, or <see langword="null"/>.</summary>
    public string? WordLink { get; init; }
}

/// <summary>Which word to trace against the current Baseline grammar.</summary>
public sealed record WordTraceRequest(string ProjectPath, string Word);

/// <summary>The parser response plus the complete portable diagnostic envelope.</summary>
public sealed record WordTraceResponse(
    string Word,
    bool Parsed,
    bool Complete,
    string? StopReason,
    int StepCount,
    string? DeepestRule,
    int ElapsedMs,
    IReadOnlyList<TraceCandidate> Candidates,
    TraceStep Root)
{
    public long? ParserSteps { get; init; }
    public double? ParserElapsedMs { get; init; }
    public bool Guessed { get; init; }
    public IReadOnlyList<TraceEffort> Effort { get; init; } = [];
    public string DiagnosticJson { get; init; } = string.Empty;
    public string DiagnosticFormat { get; init; } = string.Empty;
    public string SearchStatus { get; init; } = "complete";
    public bool InvalidShape { get; init; }
    public IReadOnlyList<TraceAnalysis> Analyses { get; init; } = [];
    public TraceHostCapture? HostCapture { get; init; }
    public TraceProvenanceComparison? Provenance { get; init; }
    public string? ParserName { get; init; }
    public string? ParserVersion { get; init; }
    public string? TraceProfile { get; init; }
    public string? GrammarHash { get; init; }
    public string? GrammarHashSemantics { get; init; }
}

public sealed record TraceEffort(
    string Kind, long Attempts, long Outputs, long NotApplied, long NoRoot, long SurfaceMismatch, long Uses, double? SelfMs)
{
    public long Work { get; init; }
    public bool TimingAvailable => SelfMs is not null;
}

public sealed record TraceAnalysis(
    string? AnalysisId,
    int? Index,
    string? Surface,
    string Availability,
    IReadOnlyList<TraceMorph> Morphs)
{
    public string? LegacyMorphemes { get; init; }
    public string? ProjectionStatus { get; init; }
    public string? ProjectionError { get; init; }
}

public sealed record TraceMorph(
    string? Identity,
    string? Form,
    string? Headword,
    string? Gloss,
    string? Category,
    string? Slot,
    string? InflectionClass,
    string? Features,
    string? GuessedString,
    string? FieldWorksLink)
{
    public string? FormId { get; init; }
    public string? EntryId { get; init; }
    public string? MsaId { get; init; }
    public string? InflTypeId { get; init; }
    public string? IdentityQuality { get; init; }
    public string? FormWritingSystem { get; init; }
    public string? HeadwordWritingSystem { get; init; }
    public string? GlossWritingSystem { get; init; }
    public string? CategoryId { get; init; }
    public string? CategoryName { get; init; }
    public string? CategoryAbbreviation { get; init; }
    public string? SlotId { get; init; }
    public bool? SlotOptional { get; init; }
    public string? InflectionClassId { get; init; }
    public string? InflectionClassName { get; init; }
    public string? InflectionClassAbbreviation { get; init; }
    public string? FeaturesSource { get; init; }
    public string? FeaturesStatus { get; init; }
    public string? RawJson { get; init; }
}

public sealed record TraceCandidate(
    IReadOnlyList<ParserReadingMorph> Morphs,
    bool Succeeded,
    string? FailureReason,
    string? Explanation,
    IReadOnlyList<TraceStep> Steps)
{
    public string? AttemptId { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
    public string MorphAvailability { get; init; } = "unavailable";
    public IReadOnlyList<TraceMorph> RichMorphs { get; init; } = [];

    /// <summary>The form this attempt had built when it ended, or <see langword="null"/> when none was recorded.</summary>
    public string? Surface { get; init; }

    /// <summary>
    /// The rule whose step failed just before this attempt ended, by the name the project gives it; <see langword="null"/>
    /// when the attempt failed on its own terms, such as leaving morphemes unused.
    /// </summary>
    public string? StoppedByRule { get; init; }

    /// <summary>The trace's own identifier for <see cref="StoppedByRule"/>, a FieldWorks GUID for an authored rule.</summary>
    public string? StoppedByRuleId { get; init; }
}
public sealed record TraceStep(
    string Type,
    string? Source,
    string? Input,
    string? Output,
    string? FailureReason,
    IReadOnlyList<TraceStep> Children)
{
    public int? Subrule { get; init; }
    public string? OutcomeStatus { get; init; }
    public string? OutcomeEventType { get; init; }
    public string? ContextualFailure { get; init; }
    public string? FailureRequired { get; init; }
    public string? FailureActual { get; init; }
    public string? FailureEnvironment { get; init; }
    public IReadOnlyList<TraceMorph> AttemptedMorphs { get; init; } = [];
    public string? SourceIdentityKind { get; init; }
    public string? SourceIdentityId { get; init; }
    public string? SourceIdentityQuality { get; init; }
}

public sealed record TraceHostCapture(
    string? ProjectIdentity,
    string? GrammarHash,
    string? GrammarHashSemantics,
    string? BundleDigest,
    DateTimeOffset? CapturedUtc,
    long? WallElapsedMs,
    IReadOnlyList<TraceWritingSystem> WritingSystems);

public sealed record TraceWritingSystem(
    string Id,
    string? Name,
    bool IsVernacular,
    bool IsDefault,
    string? Direction,
    string? Font);

public sealed record TraceProvenanceComparison(
    string ProjectIdentityStatus,
    string GrammarStatus,
    string WritingSystemsStatus,
    bool IsCompatible,
    string Warning)
{
    public bool CanNavigate { get; init; }
}
