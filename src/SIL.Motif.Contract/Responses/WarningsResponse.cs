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
    /// The Selection's words that use something these findings name, each counted once; <see langword="null"/>
    /// when no stored Parse all words matches the current Baseline and Selection.
    /// </summary>
    public WarningWordsTouched? YourWords { get; init; }

    /// <summary>The number of findings after the requested filters.</summary>
    public int TotalCount => Findings.Count;
}
