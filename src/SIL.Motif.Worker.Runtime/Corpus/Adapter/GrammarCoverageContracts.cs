using System.Text.Json.Serialization;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.Worker.Corpus.Adapter;

internal sealed record CoverageLimitation(string Code, long? Count = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null);
internal sealed record BucketCounts(
    long? Analysed, [property: JsonPropertyName("no-analysis")] long? NoAnalysis, long? Misspelled, long? Ambiguous, long? Disposition, long? Unmeasured);
internal sealed record ReachCounts(long? Grammar, long? PackAccepted, long? AnyAccepted, long? Measured);
internal sealed record CorrectnessCounts(
    long? ApprovedOccurrences = null, long? Checked = null, long? Matched = null, long? WrongOnly = null,
    long? Missed = null, long? Ambiguous = null, long? Unmeasured = null, double? Accuracy = null,
    string? Qualification = null, string? UnavailableReason = "judgement-evidence-unavailable");
internal sealed record JudgementCounts(
    long? Approved = null, long? Guess = null, long? Conflict = null, [property: JsonPropertyName("changed-since")] long? ChangedSince = null,
    long? Stale = null, long? None = null);
internal sealed record DispositionCounts(
    long? Name = null, long? Loan = null, [property: JsonPropertyName("other-language")] long? OtherLanguage = null, long? Abbreviation = null,
    long? Numeral = null, [property: JsonPropertyName("non-word")] long? NonWord = null);
internal sealed record CorpusStamp(
    string Id, string Revision, string? Baseline, string ScopeDigest, string SourceDigest, string SegmentationId,
    string? GrammarId, string? ParserId, IReadOnlyList<string> ResourceIds, string? LimitsId,
    string? JudgementRevision, string AggregationPolicy, int SchemaVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceGeneration = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ComparisonVersion = null);
internal sealed record CoverageAggregate(
    long Occurrences, long DistinctWordforms, BucketCounts OccurrenceBuckets, BucketCounts WordformBuckets,
    DispositionCounts Dispositions, ReachCounts ReachOccurrences, ReachCounts ReachWordforms,
    CorrectnessCounts Correctness, JudgementCounts Judgements, long DigitOnlyOccurrences,
    long OtherWritingSystemOccurrences, long? InvalidShapeOccurrences, long ExcludedOccurrences, long? Open, long? ApprovedLost);
internal sealed record WordformCoverage(
    WordformKey Wordform, string WordformId, long Frequency, BucketCounts Buckets, DispositionCounts Dispositions,
    ReachCounts Reach, CorrectnessCounts Correctness, JudgementCounts Judgements, IReadOnlyList<ReadLimitation> Limitations)
{
    public string Row => "coverage";
}
