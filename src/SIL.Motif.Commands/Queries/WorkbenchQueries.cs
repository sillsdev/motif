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
public sealed record TextWord(
    string Form,
    string? WordformGuid,
    IReadOnlyList<WordOccurrence> Occurrences,
    IReadOnlyList<ProjectAnalysis> Approved,
    IReadOnlyList<ProjectAnalysis> Disapproved);

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

/// <summary>Which word to trace, against which project's current Baseline grammar.</summary>
public sealed record WordTraceRequest(string ProjectPath, string Word);

/// <summary>
/// The parser's own account of one word: the candidate morph sequences it tried and why each failed, as
/// FieldWorks' Try a Word reports them, and the full derivation tree behind them.
/// </summary>
/// <param name="Word">The word traced.</param>
/// <param name="Parsed">Whether any candidate succeeded.</param>
/// <param name="Complete">False when the trace stopped early; <paramref name="StopReason"/> then says why.</param>
/// <param name="StopReason">Why the trace stopped early, or <see langword="null"/> when it finished.</param>
/// <param name="StepCount">How many derivation steps the trace holds.</param>
/// <param name="DeepestRule">The deepest rule the derivation reached, or <see langword="null"/>.</param>
/// <param name="ElapsedMs">How long the trace took.</param>
/// <param name="Candidates">Each candidate sequence, successes first, then failures by depth reached.</param>
/// <param name="Root">The derivation tree.</param>
public sealed record WordTraceResponse(
    string Word,
    bool Parsed,
    bool Complete,
    string? StopReason,
    int StepCount,
    string? DeepestRule,
    int ElapsedMs,
    IReadOnlyList<TraceCandidate> Candidates,
    TraceStep Root);

/// <summary>One morph sequence the parser tried for a word.</summary>
/// <param name="Morphs">The morphs of the sequence, as a person reads them.</param>
/// <param name="Succeeded">Whether this sequence produced the word.</param>
/// <param name="FailureReason">The parser's failure reason code, or <see langword="null"/> when it succeeded.</param>
/// <param name="Explanation">The failure in plain language, as FieldWorks words it, or <see langword="null"/>.</param>
/// <param name="Steps">The steps along this candidate's path, root first, each marked as passing or failing.</param>
public sealed record TraceCandidate(
    IReadOnlyList<ParserReadingMorph> Morphs,
    bool Succeeded,
    string? FailureReason,
    string? Explanation,
    IReadOnlyList<TraceStep> Steps);

/// <summary>One node of the derivation tree.</summary>
/// <param name="Type">The parser's node type, such as <c>MorphologicalRuleSynthesis</c>.</param>
/// <param name="Source">The rule, stratum or template the step belongs to, or <see langword="null"/>.</param>
/// <param name="Input">The form entering the step, or <see langword="null"/>.</param>
/// <param name="Output">The form leaving the step, or <see langword="null"/>.</param>
/// <param name="FailureReason">Why the step failed, or <see langword="null"/> when it did not.</param>
/// <param name="Children">The steps beneath this one.</param>
public sealed record TraceStep(
    string Type,
    string? Source,
    string? Input,
    string? Output,
    string? FailureReason,
    IReadOnlyList<TraceStep> Children);
