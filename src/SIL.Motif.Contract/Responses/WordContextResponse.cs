using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>A word's complete stored context, independent of Selection membership or parser measurements.</summary>
public sealed record WordContextResponse(string Word, bool HasBaseline)
{
    /// <summary>The exact Baseline supplying these analyses; null before a capture.</summary>
    public BaselineToken? Baseline { get; init; }
    /// <summary>The FieldWorks save represented by these analyses.</summary>
    public DateTimeOffset? SourceLastWriteUtc { get; init; }
    /// <summary>When this Baseline was published.</summary>
    public DateTimeOffset? PublishedUtc { get; init; }
    /// <summary>Whether the live file has been saved since this Baseline; its stored context remains readable.</summary>
    public bool IsStale { get; init; }
    /// <summary>Exact wordform membership, including wordforms without analyses; null when no Baseline was read.</summary>
    public bool? IsInFieldWorks { get; init; }
    /// <summary>Every stored analysis with its own opinion, identity and resolved morphemes.</summary>
    public IReadOnlyList<ParserReading> Analyses { get; init; } = [];
    /// <summary>The limited comparison fallback: first approved analysis, else the sole stored analysis.</summary>
    public ParserReading? ExpectedAnalysis { get; init; }
    /// <summary>The exact wordform's Word Analyses link, or null when it is absent.</summary>
    public string? WordAnalysesLink { get; init; }
}
