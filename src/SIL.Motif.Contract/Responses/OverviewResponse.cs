using SIL.Motif.Contract.Baselines;

namespace SIL.Motif.Contract.Responses;

/// <summary>The project header and metrics shown by the Overview page and <c>motif overview</c>.</summary>
public sealed record OverviewResponse(
    string ProjectName,
    DateTimeOffset MotifStoreCreatedUtc,
    DateTimeOffset? LastFieldWorksSaveUtc,
    int SelectionWordCount,
    int SelectionTextCount,
    int SelectionAddedWordCount,
    int TextOccurrenceCount,
    int WordformCount,
    int RuleCount,
    int LexemeCount,
    string? AssessmentId,
    DateTimeOffset? AssessedUtc,
    double? AssessmentElapsedSeconds,
    string? GrammarFingerprint,
    string? SelectionFingerprint,
    OverviewTextCoverage TextCoverage,
    OverviewAccuracy Accuracy,
    OverviewTiming Timing,
    OverviewWarningsSummary? Warnings)
{
    /// <summary>Whether the default Selection resolved against the current Baseline.</summary>
    public bool SelectionResolved { get; init; }

    /// <summary>The share of resolved Selection words with a completed parse.</summary>
    public double? WordCoveragePercent { get; init; }

    /// <summary>The file name of the FieldWorks project.</summary>
    public string ProjectFileName { get; init; } = string.Empty;

    /// <summary>When the current Baseline was captured.</summary>
    public DateTimeOffset? BaselineCapturedUtc { get; init; }

    /// <summary>The FieldWorks file's last-write time when the Baseline was captured.</summary>
    public DateTimeOffset? BaselineSourceLastWriteUtc { get; init; }

    /// <summary>Whether the FieldWorks file has changed since the current Baseline.</summary>
    public bool IsStale { get; init; }

    /// <summary>The current Baseline fingerprints, or <see langword="null"/> before the first capture.</summary>
    public BaselineToken? BaselineToken { get; init; }
}

/// <summary>How many words and Text occurrences in the Selection produced a completed parse.</summary>
public sealed record OverviewTextCoverage(
    int ParsedWords,
    int NoParseWords,
    int UnknownWords,
    int SkippedWords,
    int TotalOccurrences,
    int ParsedOccurrences)
{
    /// <summary>The share of selected Text occurrences with a completed parse.</summary>
    public double? OccurrenceCoveragePercent { get; init; }
}

/// <summary>Word counts placed by the Compare matrix's shared classification rules.</summary>
public sealed record OverviewAccuracy(
    int ApprovedWordsKept,
    int ApprovedWordCount,
    int Violations,
    int UnknownWords,
    int RejectedAnalysesRebuilt,
    int RejectedWordCount,
    int CandidatesConfirmed,
    int CandidateWordCount)
{
    /// <summary>Rejected-standing words placed in the Compare matrix's Match column.</summary>
    public int RejectedWordsInMatchCell { get; init; }

    /// <summary>Approved-standing words placed in the Compare matrix's NoMatch column.</summary>
    public int ApprovedWordsNoMatch { get; init; }

    /// <summary>Approved-standing words placed in the Compare matrix's NoParse column.</summary>
    public int ApprovedWordsNoParse { get; init; }

    /// <summary>Approved-standing words placed in the Compare matrix's Timeout column.</summary>
    public int ApprovedWordsUnknown { get; init; }

    /// <summary>Approved-standing words placed in the Compare matrix's Skipped column.</summary>
    public int ApprovedWordsSkipped { get; init; }
}

/// <summary>Per-word parse-time percentiles, slowest words, and step-limited count.</summary>
public sealed record OverviewTiming(
    double? MedianMs,
    double? Percentile95Ms,
    IReadOnlyList<SlowWordTiming> SlowestWords,
    int StepLimitedWordCount);

/// <summary>A word among the slowest measured words.</summary>
public sealed record SlowWordTiming(string Word, int ElapsedMs);

/// <summary>Counts of grammar findings and the largest diagnostic category.</summary>
public sealed record OverviewWarningsSummary(int? Count, int? LeftOut, string? LargestKind, int? LargestKindCount)
{
    /// <summary>The number of findings whose report level is error.</summary>
    public int? ErrorCount { get; init; }

    /// <summary>The number of findings whose report level is warning.</summary>
    public int? WarningCount { get; init; }

    /// <summary>The number of findings whose report level is information.</summary>
    public int? InformationCount { get; init; }

    /// <summary>Finding counts by the report's stable diagnostic code.</summary>
    public IReadOnlyList<GrammarWarningSummary> ByKind { get; init; } = [];
}
