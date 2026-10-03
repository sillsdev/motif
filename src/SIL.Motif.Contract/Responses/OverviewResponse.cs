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
    /// <summary>The stored results worth opening first; empty when no Parse all words matches.</summary>
    public OverviewLookFirst LookFirst { get; init; } = OverviewLookFirst.Empty;

    /// <summary>The producing run and measurement time for each word contributing to these composite metrics.</summary>
    public IReadOnlyDictionary<string, WordMeasurementOrigin> WordOrigins { get; init; } =
        new Dictionary<string, WordMeasurementOrigin>(StringComparer.Ordinal);

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
    /// <summary>Words where PanGloss built the same analysis as FieldWorks.</summary>
    public int SameWords { get; init; }

    /// <summary>Words where PanGloss built an analysis different from FieldWorks.</summary>
    public int DifferentWords { get; init; }

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
    int StepLimitedWordCount)
{
    /// <summary>The number of words with a recorded parse time, which the percentiles are taken over.</summary>
    public int MeasuredWordCount { get; init; }

    /// <summary>The measured words' object time by kind, each a share of their total word time.</summary>
    public IReadOnlyList<TimingAggregateRow> Kinds { get; init; } = [];

    /// <summary>The measured words' total word time and the part of it no parser object recorded.</summary>
    public WordTimeAttribution Attribution { get; init; } = WordTimeAttribution.None;
}

/// <summary>Stored word groups that give a linguist a useful next place to look.</summary>
public sealed record OverviewLookFirst(
    IReadOnlyList<string> ApprovedLostWords,
    IReadOnlyList<OverviewSharedMorpheme> SharedLostMorphemes,
    IReadOnlyList<string> StepLimitedWords,
    double? StepLimitedWordTimeMs,
    int UnknownDifferentWordCount)
{
    /// <summary>Approved words whose recorded analyses share forms and glosses but name different entries.</summary>
    public IReadOnlyList<string> ApprovedSameTextDifferentEntryWords { get; init; } = [];

    /// <summary>Whether the Baseline entries needed to identify that group were read.</summary>
    public bool ApprovedSameTextDifferentEntryAvailable { get; init; } = true;

    /// <summary>Whether shared lost-morpheme associations were read; false means unknown, rather than none.</summary>
    public bool SharedLostMorphemesAvailable { get; init; }

    /// <summary>An empty list of priorities when the project has no matching Parse all words.</summary>
    public static OverviewLookFirst Empty { get; } = new([], [], [], null, 0);
}

/// <summary>A morpheme used by several lost Approved words, matched by FieldWorks identity.</summary>
/// <param name="Form">The allomorph form in the stored analysis.</param>
/// <param name="WordCount">How many lost words use it.</param>
/// <param name="NamedByWarning">Whether a stored grammar warning names this allomorph or grammatical info.</param>
public sealed record OverviewSharedMorpheme(string Form, int WordCount, bool NamedByWarning);

/// <summary>A word among the slowest measured words, with its exact parse duration in milliseconds.</summary>
public sealed record SlowWordTiming(string Word, double ElapsedMs);

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

    /// <summary>
    /// The Selection's words that exactly use something a finding names, each counted once, with membership and
    /// spelling candidates counted separately; <see langword="null"/> when no stored Parse all words matches the
    /// current Baseline and Selection.
    /// </summary>
    public WarningWordsTouched? YourWords { get; init; }
}
