using System.Text.Json.Serialization;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Worker.Corpus.Adapter;

namespace SIL.Motif.Worker.Corpus;

internal sealed record FrequencyProvenance(
    string ProjectId, string SourceId, string SourceDigest, string ScopeDigest, string WeightProvenance,
    string TokenizerId, string TokenizerHash, string ProfileHash, bool Fold, string CaseFallback,
    string FormNormalization, string WritingSystemId, IReadOnlyDictionary<string, long> Exclusions, bool AllowsSurfaceStorage,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ParatextFrequencyScope? ParatextScope = null);

internal sealed record ParatextFrequencyScope(
    string Generation, string Context, string Versification, string AnchorVersion, string PassageDigest);

internal sealed record FrequencyRow(
    string WordformId, string WritingSystemId, string? CasedForm, string ParserKey, long Frequency,
    string? FallbackParserKey = null);

internal sealed record FrequencyManifest(FrequencyProvenance Provenance, IReadOnlyList<FrequencyRow> Rows);

internal sealed record GrammarCoverageBasis(
    string ProjectId, string Revision, BaselineToken Baseline, string GrammarId, string ParserId, string LimitsId,
    string SegmentationId, string MappingDigest, string? ImportedSnapshotDigest = null, string? SourceGeneration = null);

internal sealed record AssessmentInputBinding(
    string SelectionDigest, string ParsedWordsDigest, BatchInputProfile Profile);

internal sealed record GrammarCoverageSummary(
    CorpusStamp Stamp, string AssessmentId, FrequencyProvenance Provenance, string MappingDigest, string WeightDigest,
    CoverageAggregate Aggregate, AssessmentInputBinding AssessmentInputs, BucketCounts ProvedOccurrences, BucketCounts ProvedWordforms,
    GrammarCoverageUnknownCounts UnknownOccurrences, GrammarCoverageUnknownCounts UnknownWordforms,
    long ProvedInvalidShapeOccurrences, long ProvedInvalidShapeWordforms,
    long RankedWordforms, string Completion, IReadOnlyList<CoverageLimitation> Limitations);

internal sealed record GrammarCoverageUnknownCounts(
    long Analysed, long NoAnalysis, long Ambiguous, long InvalidShape, long Misspelled, long Disposition,
    long Open, long ApprovedLost, long PackAccepted, long AnyAccepted);

internal sealed record GrammarCoverageSegmentation(string Id, string MappingDigest, string WeightDigest,
    string? SourceGeneration = null);

internal interface IGrammarCoverageReader
{
    GrammarCoverageSummary ReadSummary(string stampId, CancellationToken cancellationToken = default);

    GrammarCoveragePage ReadNoAnalysis(string stampId, PageRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record GrammarCoveragePage(CorpusStamp Stamp, long RankedWordforms, Page<WordformCoverage> Page,
    IReadOnlyDictionary<string, long> LimitationWeights);

internal enum GrammarCoverageReadFailure { Unavailable, ExpiredPin, Busy, InvalidCursor, InvalidRequest }

internal sealed class GrammarCoverageReadException(GrammarCoverageReadFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public GrammarCoverageReadFailure Failure { get; } = failure;
}
