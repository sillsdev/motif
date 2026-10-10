using SIL.Motif.Contract.Responses;
using System.Text.Json.Serialization;

namespace SIL.Motif.Host.Texts;

/// <summary>Compact source positions and word identities used to open a saved Text Selection.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BaselineTextReadIndex(
    Guid TextId,
    string Title,
    string? TitleWritingSystem,
    IReadOnlyList<BaselineTextReadLine> Lines,
    IReadOnlyList<BaselineTextReadWordform> Wordforms);

/// <summary>One captured source line without its sentence or display graph.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BaselineTextReadLine(
    int Number,
    Guid ParagraphId,
    Guid SegmentId,
    bool ParseIsCurrent,
    IReadOnlyList<BaselineTextReadToken> Tokens);

/// <summary>One source token's stable position, status, analysis and exact writing-system forms.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BaselineTextReadToken(
    int OccurrenceIndex,
    Guid? WordformId,
    string? Status,
    string? AnalysisKey,
    Guid? AnalysisId,
    IReadOnlyList<WritingSystemText> Forms);

/// <summary>Classification counts and semantic analysis identities for one Text's wordform.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BaselineTextReadWordform(
    Guid WordformId,
    int ApprovedCount,
    int CandidateCount,
    int DisapprovedCount,
    bool IncorrectSpelling,
    IReadOnlyList<BaselineTextReadAnalysis> Analyses);

/// <summary>An analysis digest and its ordered semantic matching identity.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record BaselineTextReadAnalysis(
    string Key,
    Guid AnalysisId,
    string Opinion,
    ApprovedMorphology Identity);
