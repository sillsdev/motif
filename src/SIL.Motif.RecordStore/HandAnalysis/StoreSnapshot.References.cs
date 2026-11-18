using System.Collections.Immutable;
using System.Globalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;
using Witness = SIL.Motif.Contract.HandAnalysis.ReferenceWitness;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal sealed partial class StoreSnapshot
{
    internal DeletionImpact PreviewDeletion(Guid target) => Point.Index.DeletionImpact(target);

    public ValueTask<CommandOutcome<ReferencePage>> ReferencesAsync(ReferenceQuery query, PageRequest page, CancellationToken ct = default) =>
        Read(query, page, ct, (point, request) => References(point.Index, query, request, ct));

    private ReferencePage References(ReferenceIndexSnapshot index, ReferenceQuery query, PagePosition request, CancellationToken ct)
    {
        var schema = index.UsageSchema!;
        switch (query)
        {
            case ReferenceQuery.DeleteClosure { Target: var target }:
                var record = index.Record(target.Id);
                if (record is null)
                    throw new QueryRefused(new(RefusalCodes.HandAnalysisIncompleteReferences, FailureReason.Refused,
                        "The deletion target is unavailable."));
                if (!schema.Is(record.ClassId, NativeClass(target.Kind)))
                    throw new QueryRefused(new(RefusalCodes.HandAnalysisTypeMismatch, FailureReason.Refused,
                        "The deletion kind does not match the target's native class."));
                var impact = index.DeletionImpact(target.Id);
                return Rows(index, request, impact.Links, impact.Witnesses, impact.Complete, impact.Limitations,
                    impact.Targets, impact.Bundles.Length, impact.Analyses.Length, [], ct, impact.PlanToken);
            case ReferenceQuery.Uses uses:
                var targetIds = uses.Targets.Distinct().OrderBy(t => t.Id).ThenBy(t => t.Kind).ToImmutableArray();
                var targets = targetIds.Where(t => index.Record(t.Id) is { } source &&
                    schema.Is(source.ClassId, NativeClass(t.Kind))).Select(t => t.Id).Distinct().ToArray();
                var references = index.Incoming(targets, uses.IncludeDescendants);
                var missing = references.Limitations;
                if (targetIds.Any(t => index.Record(t.Id) is not { } native || !schema.Is(native.ClassId, NativeClass(t.Kind))))
                    missing = missing.Add("A typed target does not identify a known native record.");
                if (uses.Scope is not null) missing = missing.Add("Occurrence scope is unavailable in this index.");
                var links = references.Native.Where(e => !e.Owning).ToImmutableArray();
                var witnesses = uses.Scope is null ? references.Witnesses : ImmutableArray<ReferenceWitness>.Empty;
                var members = links.Select(l => l.Source).Concat(witnesses.Select(w => w.NativeSource ?? w.Target))
                    .Distinct().Select(index.WfiOwners).ToArray();
                return Rows(index, request, links, witnesses, references.UsageComplete && missing.IsEmpty,
                    missing, references.Targets, members.Select(l => l.Bundle).OfType<Guid>().Distinct().Count(),
                    members.Select(l => l.Analysis).OfType<Guid>().Distinct().Count(), targetIds, ct);
            default: throw new ArgumentException("Unknown reference query.", nameof(query));
        }
    }

    private ReferencePage Rows(ReferenceIndexSnapshot index, PagePosition position, ImmutableArray<NativeReference> links,
        ImmutableArray<ReferenceWitness> witnesses, bool complete, ImmutableArray<string> missing, ImmutableArray<Guid> affected,
        int bundles, int analyses, ImmutableArray<ReferenceTarget> targets, CancellationToken ct, string? closureDigest = null)
    {
        var rows = links.Select(l =>
            {
                ct.ThrowIfCancellationRequested();
                return Fact(index, l.Target, l.Source, Location(index.UsageSchema!, l), null);
            })
            .Concat(witnesses.Select(w =>
            {
                ct.ThrowIfCancellationRequested();
                return Fact(index, w.Target, w.NativeSource, null, w);
            }))
            .Select((fact, i) => (Key: (long)i, Fact: fact)).ToArray();
        var limits = missing.Select(l => new ReadLimitation("closure-incomplete", affected, l)).ToImmutableArray();
        var usage = targets.Select(t => new TargetUsage(ResultTarget(index, t.Id), null, null, null, null)).ToImmutableArray();
        var counts = new ReferenceCounts(complete ? bundles : null, complete ? analyses : null,
            complete ? ReferenceWitness.CountOccurrences(witnesses) : null, usage, closureDigest, complete,
            complete ? closureDigest : null);
        var page = Paginate(position, rows, r => r.Key, limits, complete);
        return new(new(Stamp, page.Items.Select(i => i.Fact).ToImmutableArray(), page.Total, page.NextCursor, page.Complete, limits), counts);
    }

    private ReferenceFact Fact(ReferenceIndexSnapshot index, Guid target, Guid? referrer, ReferenceLocation? location,
        ReferenceWitness? witness)
    {
        var (bundle, analysis, wordform) = referrer is { } id ? index.WfiOwners(id) : default;
        var result = ResultTarget(index, target);
        var source = referrer is { } sourceId ? ResultTarget(index, sourceId) : null;
        ImmutableArray<Witness> occurrences = witness is null ? [] :
            [new(WitnessKind(witness.Kind), witness.Identity, witness.EvidenceRevision, result, source, witness.Location, null)];
        var state = witness?.Kind switch
        {
            ReferenceWitnessKind.ConflictingHead => ReferenceState.Conflicting,
            ReferenceWitnessKind.ParserReading => ReferenceState.Unknown,
            _ => ReferenceState.Current
        };
        return new(Stamp, result, source, location, bundle, analysis, wordform, occurrences, state);
    }

    private static ReferenceLocation Location(NativeUsageSchema schema, NativeReference edge)
    {
        var field = schema.FieldName(edge.Field) ?? edge.Field.ToString(CultureInfo.InvariantCulture);
        var occurrence = edge.Occurrence;
        string? ws = null;
        var runStart = 0;
        if (occurrence.StartsWith("ws:", StringComparison.Ordinal) && occurrence.IndexOf('/') is var slash && slash > 3)
        {
            ws = Uri.UnescapeDataString(occurrence[3..slash]);
            runStart = slash + 1;
        }
        if (occurrence.AsSpan(runStart).StartsWith("run:", StringComparison.Ordinal) &&
            int.TryParse(occurrence.AsSpan(runStart + 4), NumberStyles.None, CultureInfo.InvariantCulture, out var run))
            return new(field, null, null, ws, run);
        return new(field, edge.Position >= 0 ? edge.Position : null, occurrence.Length > 0 ? occurrence : null, null, null);
    }

    private static ReferenceResultTarget ResultTarget(ReferenceIndexSnapshot index, Guid id) =>
        new(id, index.Record(id) is { } native ? index.UsageSchema!.ClassName(native.ClassId) : null);

    private static string WitnessKind(ReferenceWitnessKind kind) => kind switch
    {
        ReferenceWitnessKind.Anchor => "anchor",
        ReferenceWitnessKind.ConflictingHead => "conflicting-head",
        ReferenceWitnessKind.ParserReading => "parser-reading",
        _ => kind.ToString()
    };

    private static string NativeClass(DeletionKind kind) => kind switch
    {
        DeletionKind.Entry => "LexEntry",
        DeletionKind.Sense => "LexSense",
        DeletionKind.Allomorph => "MoForm",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static string NativeClass(ReferenceKind kind) => kind switch
    {
        ReferenceKind.Wordform => "WfiWordform",
        ReferenceKind.Analysis => "WfiAnalysis",
        ReferenceKind.Gloss => "WfiGloss",
        ReferenceKind.Bundle => "WfiMorphBundle",
        ReferenceKind.Entry => "LexEntry",
        ReferenceKind.Sense => "LexSense",
        ReferenceKind.Allomorph => "MoForm",
        ReferenceKind.Msa => "MoMorphSynAnalysis",
        ReferenceKind.EntryRef => "LexEntryRef",
        ReferenceKind.InflectionalType => "LexEntryInflType",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
