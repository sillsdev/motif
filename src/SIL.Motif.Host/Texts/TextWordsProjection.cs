using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// The Text words data derived from one saved Baseline: its Texts, and every wordform their tokens use. It is
/// stored one Text and one wordform per row, so the Texts page reads only what the chosen Texts need.
/// </summary>
public sealed record TextWordsProjection(
    IReadOnlyList<TextWordsProjectedText> Texts,
    IReadOnlyList<TextWordsProjectedWordform> Wordforms);

/// <summary>
/// One Text with its ordered lines, and each analysis its tokens chose, stored once and referred to by key.
/// </summary>
public sealed record TextWordsProjectedText(
    Guid TextId,
    string Title,
    IReadOnlyList<TextWordsProjectedLine> Lines,
    IReadOnlyList<TextWordsProjectedAnalysis> Analyses);

/// <summary>One line in a Text, retaining its source sentence and ordered token evidence.</summary>
public sealed record TextWordsProjectedLine(
    int Number,
    string Sentence,
    IReadOnlyList<TextWordsProjectedToken> Tokens);

/// <summary>
/// One source token and all ordered wordform alternatives that contribute to Text words. A word token names its
/// chosen analysis by the key of an entry in its Text's <see cref="TextWordsProjectedText.Analyses"/>.
/// </summary>
public sealed record TextWordsProjectedToken(
    string Text,
    IReadOnlyList<string> Forms,
    Guid? WordformId,
    string? Status,
    string? AnalysisKey,
    string? WordGloss,
    string? Category,
    FieldWorksLinkTarget? WordLinkTarget);

/// <summary>Project-level analysis standing for one wordform in the Baseline.</summary>
public sealed record TextWordsProjectedWordform(
    Guid WordformId,
    IReadOnlyList<TextWordsProjectedAnalysis> Approved,
    IReadOnlyList<TextWordsProjectedAnalysis> Disapproved,
    int CandidateCount,
    bool IncorrectSpelling);

/// <summary>
/// One stored analysis and its ordered display-ready morphology. Its key is the analysis content digest, so two
/// analyses sharing a key share their morphs and display the same.
/// </summary>
public sealed record TextWordsProjectedAnalysis(
    string Key,
    IReadOnlyList<TextWordsProjectedMorph> Morphs);

/// <summary>One display-ready morph with a stable target for a request-specific FieldWorks link.</summary>
public sealed record TextWordsProjectedMorph(
    string Form,
    string Gloss,
    string Category,
    string? InflectionType,
    bool Guessed,
    FieldWorksLinkTarget? LinkTarget);
