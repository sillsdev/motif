using System.Collections.Immutable;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.Worker.Corpus.Adapter;

internal static class GrammarCoverageWire
{
    public static GrammarCoveragePage RankedPage(GrammarCoverageSummary summary, IEnumerable<WordformCoverage> items,
        long? total, string? cursor)
    {
        var corpus = summary.Stamp;
        if (!Guid.TryParseExact(summary.Provenance.ProjectId, "D", out var projectId) || projectId == Guid.Empty)
            throw new InvalidDataException("The Coverage pin does not identify a native project.");
        var parserRevision = GrammarCoverageInventory.Digest(new { corpus.GrammarId, corpus.ParserId, summary.AssessmentId });
        var settingsRevision = GrammarCoverageInventory.Digest(new
        {
            corpus.ScopeDigest, corpus.SegmentationId, corpus.LimitsId, corpus.AggregationPolicy,
            summary.MappingDigest, summary.WeightDigest, summary.AssessmentInputs.Profile,
        });
        var snapshot = new SnapshotStamp(corpus.Id, projectId, corpus.Revision,
            corpus.SourceGeneration ?? corpus.SourceDigest, parserRevision, settingsRevision,
            corpus.ComparisonVersion ?? "nfd/" + GrammarCoverageStore.RankOrder, corpus.SchemaVersion, SnapshotVerification.Full);
        var page = new Page<WordformCoverage>(snapshot, items.ToImmutableArray(), total, cursor, total.HasValue,
            summary.Limitations.Select(ToContract).ToImmutableArray());
        return new(corpus, summary.RankedWordforms, page, Weights(summary.Limitations));
    }

    public static object Summary(GrammarCoverageSummary summary) => new
    {
        summary.Stamp, summary.AssessmentId, summary.Provenance, summary.MappingDigest, summary.WeightDigest,
        summary.Aggregate, summary.AssessmentInputs, summary.ProvedOccurrences, summary.ProvedWordforms,
        summary.UnknownOccurrences, summary.UnknownWordforms, summary.ProvedInvalidShapeOccurrences,
        summary.ProvedInvalidShapeWordforms, summary.RankedWordforms, summary.Completion,
        Limitations = summary.Limitations.Select(ToContract).ToArray(),
        LimitationWeights = Weights(summary.Limitations),
    };

    public static ReadLimitation ToContract(CoverageLimitation limitation) => new(limitation.Code, [],
        limitation.Detail ?? (limitation.Code switch
        {
            "judgement-evidence-unavailable" => "The measurement has no compatible judgement evidence.",
            "pack-evidence-unavailable" => "The measurement has no compatible pack evidence.",
            "missing-check" => "The Assessment has no check for the affected Wordforms.",
            "stopped-check" => "The parser stopped at its time limit.",
            "capped-check" => "The parser reached its work or memory limit.",
            "incomplete-check" => "The saved check did not establish a completed result.",
            "reading-identity-unavailable" => "The check could not establish distinct Reading identities exhaustively.",
            "other-writing-system" => "The check used a different writing system from the affected Wordforms.",
            "invalid-shape" => "The parser refused the shape of the affected Wordforms.",
            "invalid-shape-unmeasured" => "The check could not establish whether the shapes were valid.",
            "check-evidence-unavailable" => "The affected checks lack complete morphology evidence.",
            "unsupported-grammar" => "The parser reported a grammar capability limitation for the affected checks.",
            "retry-diagnostics-unavailable" => "The Assessment did not retain diagnostics for the retry.",
            _ => "The saved Coverage publication reported " + limitation.Code + ".",
        }));

    private static Dictionary<string, long> Weights(IEnumerable<CoverageLimitation> limitations) => limitations
        .Where(limitation => limitation.Count.HasValue)
        .ToDictionary(limitation => limitation.Code, limitation => limitation.Count!.Value, StringComparer.Ordinal);
}
