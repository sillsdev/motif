namespace SIL.Motif.Contract.Responses;

/// <summary>The project header and metrics shown by the Overview page and <c>motif overview</c>.</summary>
public sealed record OverviewResponse(
    string ProjectName,
    DateTimeOffset OpenedUtc,
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
    OverviewWarningsSummary? Warnings);

/// <summary>How many words and Text occurrences in the Selection produced a completed parse.</summary>
public sealed record OverviewTextCoverage(
    int ParsedWords,
    int NoParseWords,
    int UnknownWords,
    int SkippedWords,
    int TotalOccurrences,
    int ParsedOccurrences);

/// <summary>Word counts placed by the Compare matrix's shared classification rules.</summary>
public sealed record OverviewAccuracy(
    int ApprovedWordsKept,
    int ApprovedWordCount,
    int Violations,
    int UnknownWords,
    int RejectedAnalysesRebuilt,
    int RejectedWordCount,
    int CandidatesConfirmed,
    int CandidateWordCount);

/// <summary>Per-word parse-time percentiles, slowest words, and step-limited count.</summary>
public sealed record OverviewTiming(
    double? MedianMs,
    double? Percentile95Ms,
    IReadOnlyList<SlowWordTiming> SlowestWords,
    int StepLimitedWordCount);

/// <summary>A word among the slowest measured words.</summary>
public sealed record SlowWordTiming(string Word, int ElapsedMs);

/// <summary>A reserved typed location for a future summary; warning details are not stored yet.</summary>
public sealed record OverviewWarningsSummary(int? Count, int? LeftOut, string? LargestKind, int? LargestKindCount);
