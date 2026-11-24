using System.Collections.Immutable;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>
/// Every derived fact of one pinned point, in one place. This is the shape a persisted index (S3c) stores:
/// <see cref="Wfi"/> (wordform headers, readings with use counts and rank keys, key index),
/// <see cref="Lexicon"/> (entry closures, allomorph rows, equivalence-key, marker-pair and text indexes),
/// <see cref="Occurrences"/> (slot table, attachment and wordform indexes, missing-slot position arrays),
/// <see cref="Project"/> (morph types, writing systems, category tree) and the comparer version that keyed it.
/// Nothing in it refers to a native record, a database or a lazy object.
/// </summary>
internal sealed class HandAnalysisProjection
{
    public required WfiProjection Wfi { get; init; }
    public required LexiconProjection Lexicon { get; init; }
    public required OccurrenceProjection Occurrences { get; init; }
    public required ProjectProjection Project { get; init; }
    public required string ComparisonVersion { get; init; }

    /// <summary>Rows examined by any lookup since the point was built; indexed lookups examine only their result.</summary>
    public long Touched => Lexicon.Touched + Occurrences.Touched;

    internal static ImmutableArray<WsDefinition> Systems(VerifiedPoint point) => point.WritingSystems
        .Select(r => new WsDefinition(r.Values["tag"].GetString()!, r.Id, Convert.FromBase64String(r.Values["ldml"].GetString()!),
            r.Values["collationBinding"].GetString()!)).OrderBy(s => s.Tag, StringComparer.Ordinal).ToImmutableArray();

    private IFormCollation? collation;

    private HandAnalysisProjection WithCollation(IFormCollation value) { collation = value; return this; }

    /// <summary>The equivalence key of NFD text in a writing system, from the comparer that keyed this point.</summary>
    public string CollationKey(string ws, string nfd) => (collation ?? OrdinalFormCollation.Instance).Key(ws, nfd);

    public static HandAnalysisProjection Build(VerifiedPoint point, IFormCollation collation, CancellationToken ct)
    {
        var usage = point.Index.UsageSchema;
        var systems = Systems(point);
        var wfi = WfiProjection.Build(point.Records.Loaded, usage, ct);
        var scan = TextScan.Read(point.Records.Loaded, usage ?? throw new IndexUnavailableException("The native schema proof is unavailable."), ct);
        var anchors = TextScan.Anchors(point.Anchors);
        var counts = TextScan.Counts(scan.Slots, anchors);
        wfi = wfi.WithUseCounts(counts);
        var project = ProjectProjection.Build(point.Records.Loaded, usage, systems, collation.Version, ct);
        var lexicon = LexiconProjection.Build(point.Records.Loaded, usage, wfi, project, collation, ct);
        var occurrences = OccurrenceProjection.Build(scan, anchors, counts, wfi, lexicon, project, ct);
        return new HandAnalysisProjection { Wfi = wfi, Lexicon = lexicon, Occurrences = occurrences, Project = project, ComparisonVersion = collation.Version }.WithCollation(collation);
    }
}
