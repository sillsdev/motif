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
    /// <summary>Completeness of the object timing detail used to associate words with findings.</summary>
    public ObjectTimingDetail? ObjectTimingDetail { get; init; }

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

    /// <summary>The Baseline, Selection, parser, and time recorded with the stored grammar check.</summary>
    public GrammarCheckProvenance? Provenance { get; init; }

    /// <summary>The number of findings after the requested filters.</summary>
    public int TotalCount => FindingsTotal ?? Findings.Count;

    /// <summary>The findings behind <see cref="TotalCount"/>, when <see cref="Findings"/> is one page of them; null when it is all of them.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? FindingsTotal { get; init; }

    /// <summary>How many findings precede this page; null when <see cref="Findings"/> is not a page.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? FindingsOffset { get; init; }

    /// <summary>The offset that starts the next page of findings; null at the end or when this is not a page.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? NextOffset { get; init; }

    /// <summary>The command that reads the next page with the caller's own flags; null at the end or when not set.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? NextStep { get; init; }

    /// <summary>This page with the command for the next one, built from its offset; unchanged at the end.</summary>
    public WarningsResponse WithNextStep(System.Func<int, string> step) =>
        NextOffset is { } next ? this with { NextStep = step(next) } : this;

    /// <summary>
    /// One page of the findings. The counts, the per-code rows and the word attribution still describe all of them,
    /// and each code row keeps its count, so the page never hides how many findings exist.
    /// </summary>
    public WarningsResponse Window(int offset, int limit)
    {
        var page = ResultPaging.Window(Findings, offset, limit);
        return this with
        {
            Findings = page, FindingsTotal = Findings.Count, FindingsOffset = offset,
            NextOffset = ResultPaging.NextOffset(offset, page.Count, Findings.Count),
        };
    }
}
