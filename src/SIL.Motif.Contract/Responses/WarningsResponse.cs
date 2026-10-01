namespace SIL.Motif.Contract.Responses;

/// <summary>The stored grammar findings selected by a warnings command.</summary>
public sealed record WarningsResponse(
    bool HasBaseline,
    bool HasCheck,
    IReadOnlyList<GrammarWarning> Findings,
    IReadOnlyList<GrammarWarningSummary> ByKind,
    int WarningCount,
    int InformationCount)
{
    /// <summary>The number of error-level findings after the requested filters.</summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// Exact lexical-use and recorded rule-call words, counted once. Membership and spelling candidates are
    /// separate counts on the same value and are excluded from Words, NoParse and ByMeaning. Null means no
    /// usable stored Parse all words evidence is available; a supported route with zero matches is not null.
    /// </summary>
    public WarningWordsTouched? YourWords { get; init; }

    /// <summary>The number of findings after the requested filters.</summary>
    public int TotalCount => Findings.Count;
}
