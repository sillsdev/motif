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
    /// <summary>The current Baseline's latest parser refusal, even before grammar findings exist.</summary>
    public SIL.Motif.Contract.Commands.Refusal? LastParserRefusal { get; init; }

    /// <summary>The number of error-level findings after the requested filters.</summary>
    public int ErrorCount { get; init; }

    /// <summary>
    /// The Selection's words that exactly use something these findings name, through a stored analysis or a recorded
    /// rule call, each counted once. Membership and spelling candidates are separate counts on the same value and are
    /// excluded from its exact counts. <see langword="null"/> when no stored Parse all words matches the current
    /// Baseline and Selection. Counts are known matches only when attribution is incomplete; zero then means
    /// no known match, rather than proof that no word is touched.
    /// </summary>
    public WarningWordsTouched? YourWords { get; init; }

    /// <summary>The Baseline inventory counts recorded with the completed check, or null when unknown.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int? CheckedLexemeCount { get; init; }

    /// <summary>The Baseline rule count recorded with the completed check, or null when unknown.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public int? CheckedRuleCount { get; init; }

    /// <summary>Motif advisories recorded with the completed check, separate from parser findings.</summary>
    public IReadOnlyList<GrammarCheckAdvisory> Advisories { get; init; } = [];

    /// <summary>The number of findings after the requested filters.</summary>
    public int TotalCount => Findings.Count;
}
