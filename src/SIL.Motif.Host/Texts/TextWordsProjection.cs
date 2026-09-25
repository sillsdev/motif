using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.Texts;

/// <summary>The complete ordered Text words data derived from one saved Baseline.</summary>
public sealed record TextWordsProjection(
    IReadOnlyList<TextWordsProjectedText> Texts,
    IReadOnlyList<TextWordsProjectedWordform> Wordforms);

/// <summary>One Text in its Baseline order, with the ordered lines and tokens used by Text words.</summary>
public sealed record TextWordsProjectedText(
    Guid TextId,
    string Title,
    IReadOnlyList<TextWordsProjectedLine> Lines);

/// <summary>One line in a Text, retaining its source sentence and ordered token evidence.</summary>
public sealed record TextWordsProjectedLine(
    int Number,
    string Sentence,
    IReadOnlyList<TextWordsProjectedToken> Tokens);

/// <summary>One source token and all ordered wordform alternatives that contribute to Text words.</summary>
public sealed record TextWordsProjectedToken(
    string Text,
    IReadOnlyList<string> Forms,
    Guid? WordformId,
    string? Status,
    TextWordsProjectedAnalysis? Analysis,
    string? Gloss,
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

/// <summary>One stored analysis and its ordered display-ready morphology.</summary>
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
