using System;
using System.Collections.Generic;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Requests;

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

/// <summary>Which Texts to read words from; empty reads none.</summary>
public sealed record TextWordsRequest(string ProjectPath, IReadOnlyList<Guid> TextIds);

/// <summary>
/// The words of the chosen Texts as they stand in the Baseline: one row per form and wordform identity,
/// every place that wordform occurs, and the analyses the project approves and disapproves for it.
/// Also the Texts line by line, for reading the words in place.
/// </summary>
/// <param name="HasBaseline">False when the project has no Baseline yet, so there was nothing to read.</param>
/// <param name="OccurrenceCount">The command's total number of word occurrences across the returned Texts.</param>
public sealed record TextWordsResponse(
    IReadOnlyList<TextWord> Words, IReadOnlyList<TextLines> Texts, bool HasBaseline, int OccurrenceCount = 0)
{
    public IReadOnlyList<WritingSystemDisplay> WritingSystems { get; init; } = [];
}

/// <summary>One wordform and one of its spellings in the chosen Texts; homographs keep separate rows.</summary>
/// <param name="Form">The form exactly as a Selection would send it to the parser (NFD).</param>
/// <param name="WordformGuid">The exact source wordform, or <see langword="null"/> when it has none.</param>
/// <param name="Occurrences">Every place this wordform uses the form in the chosen Texts, in text order.</param>
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
    bool IncorrectSpelling = false)
{
    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? FormWritingSystem { get; init; }

    public IReadOnlyList<ProjectAnalysis> Analyses { get; init; } = [];
}

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
    Guid TextId, string TextTitle, int Line, string Sentence, string Status, ProjectAnalysis? Analysis)
{
    /// <summary>The source paragraph's named style; Normal when none was authored.</summary>
    public string SentenceStyle { get; init; } = "Normal";

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? TextTitleWritingSystem { get; init; }

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? SentenceWritingSystem { get; init; }
}

/// <summary>One analysis the project holds, as morphs a person reads, with a key to compare it by.</summary>
/// <param name="Key">
/// Equal for two analyses exactly when FieldWorks would call them the same analysis: same morph count and, per
/// morph, the same form, grammatical info and inflection type. Sense is not part of it (ADR 0027).
/// </param>
/// <param name="Morphs">The morphs in order, with form, gloss, category and a FieldWorks link.</param>
public sealed record ProjectAnalysis(string Key, IReadOnlyList<ParserReadingMorph> Morphs)
{
    public string? StoredAnalysisId { get; init; }
    public string? StoredAnalysisOpinion { get; init; }
    public ApprovedMorphology? Identity { get; init; }
}

/// <summary>One chosen Text, line by line.</summary>
public sealed record TextLines(Guid TextId, string Title, IReadOnlyList<TextLine> Lines)
{
    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? TitleWritingSystem { get; init; }
}

/// <summary>One line of a Text: its number and its tokens in order.</summary>
public sealed record TextLine(int Number, IReadOnlyList<TextToken> Tokens)
{

    /// <summary>The source paragraph's named style; Normal when none was authored.</summary>
    public string SentenceStyle { get; init; } = "Normal";

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? SentenceWritingSystem { get; init; }

    /// <summary>The GUID of the paragraph that contains this line.</summary>
    public Guid ParagraphId { get; init; }

    /// <summary>The GUID of the Segment that supplies this line.</summary>
    public Guid SegmentId { get; init; }

    /// <summary>Whether FieldWorks has parsed the containing paragraph since its contents last changed.</summary>
    public bool ParseIsCurrent { get; init; }
}

/// <summary>One token of a line: a word, or the punctuation between words.</summary>
/// <param name="Text">The token as it appears in the line.</param>
/// <param name="Form">For a word, its form as in <see cref="TextWord.Form"/>; <see langword="null"/> for punctuation.</param>
/// <param name="Gloss">For a word, the gloss of the analysis chosen here, or <see langword="null"/>.</param>
/// <param name="Status">For a word, as <see cref="WordOccurrence.Status"/>; <see langword="null"/> for punctuation.</param>
public sealed record TextToken(string Text, string? Form, string? Gloss, string? Status)
{
    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? TextWritingSystem { get; init; }

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? FormWritingSystem { get; init; }

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? GlossWritingSystem { get; init; }

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? WordGlossWritingSystem { get; init; }

    /// <summary>The actual writing-system tag; null for composed, non-language or unresolved text.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public string? CategoryWritingSystem { get; init; }

    /// <summary>The source wordform identity, or <see langword="null"/> for punctuation.</summary>
    public Guid? WordformId { get; init; }

    /// <summary>The zero-based position in the source Segment's analysis sequence.</summary>
    public int OccurrenceIndex { get; init; }

    /// <summary>For a word, the analysis chosen at this occurrence, morph by morph; otherwise <see langword="null"/>.</summary>
    public ProjectAnalysis? Analysis { get; init; }

    public IReadOnlyList<ProjectAnalysis> StoredAnalyses { get; init; } = [];

    /// <summary>For a word, its word-level gloss at this occurrence, or <see langword="null"/> when none was chosen.</summary>
    public string? WordGloss { get; init; }

    /// <summary>For a word, the grammatical category of the analysis chosen here, or <see langword="null"/>.</summary>
    public string? Category { get; init; }

    public string? StoredAnalysisId { get; init; }

    /// <summary>Whether FieldWorks marks this wordform's spelling as incorrect.</summary>
    public bool IncorrectSpelling { get; init; }
    /// <summary>For a word, a <c>silfw:</c> link selecting its wordform in FieldWorks, or <see langword="null"/>.</summary>
    public string? WordLink { get; init; }
}

/// <summary>Which word to trace against the current Baseline grammar.</summary>
public sealed record WordTraceRequest(string ProjectPath, string Word);

/// <summary>Which saved trace file to read: a PanGloss trace document, as the window's Save diagnostic writes it.</summary>
public sealed record WordTraceLoadRequest(string DiagnosticPath);
