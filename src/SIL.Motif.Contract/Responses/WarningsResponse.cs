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
    /// <summary>The number of findings after the requested filters.</summary>
    public int TotalCount => WarningCount + InformationCount;
}
