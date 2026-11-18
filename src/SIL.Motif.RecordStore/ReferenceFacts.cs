using System.Collections.Immutable;

namespace SIL.Motif.RecordStore;

public sealed record NativeReference(Guid Source, int Field, int Position, Guid Target, bool Owning, string Occurrence = "");
public sealed record ReferenceRecord(Guid Id, int ClassId, Guid? Owner, int OwningField);
public enum ReferenceWitnessKind { Anchor, ConflictingHead, ParserReading }
public sealed record ReferenceWitness(ReferenceWitnessKind Kind, string Identity, string EvidenceRevision,
    Guid Target, Guid? NativeSource = null)
{
    public string? Location { get; init; }

    internal static long CountOccurrences(ImmutableArray<ReferenceWitness> witnesses) =>
        witnesses.DistinctBy(w => (w.Kind, w.Identity, w.EvidenceRevision)).LongCount();
}
/// <summary>
/// Caller observations at <paramref name="StoreRevision"/>. Anchor observations claim membership in that Home's
/// anchor domain. <paramref name="AnchorRevision"/> identifies the basis of the caller's anchor observations.
/// </summary>
public sealed record ReferenceEvidence(string StoreRevision, string? AnchorRevision, string? ConflictRevision,
    string? ParserRevision, ImmutableArray<ReferenceWitness> Witnesses);

public sealed class ReferenceResult
{
    internal ReferenceResult(string revision, ImmutableArray<Guid> targets, ImmutableArray<NativeReference> native,
        ImmutableArray<ReferenceWitness> witnesses, bool nativeComplete, ImmutableArray<string> limitations)
    {
        Revision = revision; Targets = targets; Native = native; Witnesses = witnesses;
        NativeComplete = nativeComplete; Limitations = limitations;
    }

    public string Revision { get; }
    public ImmutableArray<Guid> Targets { get; }
    public ImmutableArray<NativeReference> Native { get; }
    public ImmutableArray<ReferenceWitness> Witnesses { get; }
    public ImmutableArray<string> Limitations { get; }
    public bool NativeComplete { get; }
    public long? NativeTotal => NativeComplete ? Native.Length : null;
    public bool UsageComplete => NativeComplete && Limitations.IsEmpty;
    public bool Complete => UsageComplete;
    public long? Total => UsageComplete ? (long)Native.Length + Witnesses.Length : null;
    public void RequireComplete()
    {
        if (!UsageComplete) throw new InvalidOperationException("Incomplete references: " + string.Join("; ", Limitations));
    }
}

public sealed partial class ReferenceIndexSnapshot
{
    private const string AnchorCoverageUnavailable = "Anchor coverage is unavailable.";
    private readonly ImmutableDictionary<Guid, ImmutableArray<NativeReference>> incoming;
    private readonly ImmutableDictionary<Guid, ImmutableArray<Guid>> children;
    private readonly ImmutableDictionary<Guid, ReferenceRecord> records;
    private readonly ImmutableArray<string> limitations;
    private readonly ImmutableArray<ReferenceWitness> witnesses;
    private readonly bool nativeClosureComplete;
    public string Revision { get; }

    internal ReferenceIndexSnapshot(string revision, IEnumerable<NativeReference> edges,
        IEnumerable<ReferenceRecord> records, IEnumerable<string> limitations, NativeUsageSchema? usageSchema = null,
        CancellationToken ct = default)
    {
        Revision = revision;
        this.usageSchema = usageSchema;
        var materialized = edges.ToArray();
        incoming = materialized.GroupBy(e =>
        {
            ct.ThrowIfCancellationRequested();
            return e.Target;
        }).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        children = materialized.Where(e =>
        {
            ct.ThrowIfCancellationRequested();
            return e.Owning;
        }).GroupBy(e => e.Source)
            .ToImmutableDictionary(g => g.Key, g => g.Select(e => e.Target).ToImmutableArray());
        this.records = records.ToImmutableDictionary(r =>
        {
            ct.ThrowIfCancellationRequested();
            return r.Id;
        });
        var nativeLimitations = limitations.ToImmutableArray();
        nativeClosureComplete = nativeLimitations.IsEmpty;
        this.limitations = [.. nativeLimitations, AnchorCoverageUnavailable,
            "Conflicting-head coverage is unavailable.", "Parser-reference coverage is unavailable."];
        witnesses = [];
    }

    private void ValidateEvidence(ReferenceEvidence evidence, List<string> missing, bool storedAnchors = false,
        CancellationToken ct = default)
    {
        if (evidence.StoreRevision != Revision)
            throw new InvalidOperationException("Reference evidence belongs to another store revision.");
        if (evidence.Witnesses.IsDefault) throw new ArgumentException("Reference witnesses must be explicit.", nameof(evidence));
        var identities = new HashSet<(ReferenceWitnessKind, string, Guid?)>();
        foreach (var witness in evidence.Witnesses)
        {
            ct.ThrowIfCancellationRequested();
            var expected = witness.Kind switch
            {
                ReferenceWitnessKind.Anchor => evidence.AnchorRevision,
                ReferenceWitnessKind.ConflictingHead => evidence.ConflictRevision,
                ReferenceWitnessKind.ParserReading => evidence.ParserRevision,
                _ => null
            };
            if (expected is null || expected != witness.EvidenceRevision || string.IsNullOrWhiteSpace(witness.Identity) ||
                !identities.Add((witness.Kind, witness.Identity, storedAnchors ? witness.Target : null)))
                missing.Add("Unknown, duplicate or stale external witness.");
            if (!records.ContainsKey(witness.Target)) missing.Add("External witness target is unavailable.");
            if (witness.NativeSource.HasValue && !records.ContainsKey(witness.NativeSource.Value))
                missing.Add("External witness has an unknown native source.");
        }
    }

    /// <summary>Adds the Home's own anchor relationships and records whether their coverage was verified.</summary>
    internal ReferenceIndexSnapshot WithStoredAnchors(ImmutableArray<ReferenceWitness> anchors, bool coverage, CancellationToken ct)
    {
        var missing = limitations.ToList();
        if (coverage) missing.Remove(AnchorCoverageUnavailable);
        ValidateEvidence(new ReferenceEvidence(Revision, Revision, null, null, anchors), missing, storedAnchors: true, ct: ct);
        return new(this, Revision, incoming, missing.Distinct(StringComparer.Ordinal).ToImmutableArray(), anchors);
    }

    /// <summary>Adds caller observations without replacing the Home's verified witnesses or proving coverage.</summary>
    internal ReferenceIndexSnapshot WithObservations(ReferenceEvidence evidence)
    {
        var missing = limitations.ToList();
        ValidateEvidence(evidence, missing);
        if (evidence.Witnesses.IsEmpty) return this;

        var anchors = witnesses.Where(w => w.Kind == ReferenceWitnessKind.Anchor).ToLookup(w => w.Identity, StringComparer.Ordinal);
        var combined = witnesses.ToBuilder();
        foreach (var observation in evidence.Witnesses)
        {
            if (observation.Kind == ReferenceWitnessKind.Anchor)
            {
                var stored = anchors[observation.Identity];
                if (stored.Any(w => (w with { EvidenceRevision = observation.EvidenceRevision }) == observation)) continue;
                missing.Add(AnchorCoverageUnavailable);
                missing.Add(anchors.Contains(observation.Identity) ? "A caller anchor witness conflicts with a stored anchor."
                    : "A caller anchor witness is absent from the stored anchor set.");
            }
            combined.Add(observation);
        }
        return new(this, Revision, incoming, missing.Distinct(StringComparer.Ordinal).ToImmutableArray(), combined.ToImmutable());
    }

    private ReferenceIndexSnapshot(ReferenceIndexSnapshot other, string revision,
        ImmutableDictionary<Guid, ImmutableArray<NativeReference>> incoming,
        ImmutableArray<string> limitations, ImmutableArray<ReferenceWitness> witnesses)
    {
        Revision = revision; this.incoming = incoming; children = other.children; records = other.records;
        nativeClosureComplete = other.nativeClosureComplete; usageSchema = other.usageSchema;
        this.limitations = limitations; this.witnesses = witnesses;
    }

    internal NativeUsageSchema? UsageSchema => usageSchema;

    /// <summary>The same snapshot after one non-owning single-reference edit; ownership and limitations are unchanged.</summary>
    internal ReferenceIndexSnapshot WithEdge(string revision, NativeReference? removed, NativeReference? added)
    {
        var next = incoming;
        if (removed is not null && next.TryGetValue(removed.Target, out var old))
        {
            var rest = old.Remove(removed);
            next = rest.IsEmpty ? next.Remove(removed.Target) : next.SetItem(removed.Target, rest);
        }
        if (added is not null)
            next = next.SetItem(added.Target, next.TryGetValue(added.Target, out var present) ? present.Add(added) : [added]);
        return new ReferenceIndexSnapshot(this, revision, next, limitations, witnesses);
    }

    public ReferenceRecord? Record(Guid id) => records.GetValueOrDefault(id);

    public ReferenceResult Incoming(IEnumerable<Guid> targetIds, bool includeDescendants = true)
    {
        var targets = targetIds.ToHashSet();
        var missing = limitations.ToBuilder();
        if (!includeDescendants) missing.Add("Owning descendants were excluded from this query.");
        var knownTargets = targets.All(records.ContainsKey);
        if (!knownTargets) missing.Add("A requested native target is unavailable.");
        if (includeDescendants) ExpandOwnedClosure(targets);
        var native = targets.SelectMany(id => incoming.GetValueOrDefault(id, [])).OrderBy(e => e.Target)
            .ThenBy(e => e.Source).ThenBy(e => e.Field).ThenBy(e => e.Position).ThenBy(e => e.Occurrence, StringComparer.Ordinal).ToImmutableArray();
        var external = witnesses.Where(w => targets.Contains(w.Target)).OrderBy(w => w.Kind)
            .ThenBy(w => w.Identity, StringComparer.Ordinal).ThenBy(w => w.Target).ToImmutableArray();
        return new ReferenceResult(Revision, targets.Order().ToImmutableArray(), native, external,
            nativeClosureComplete && includeDescendants && knownTargets, missing.ToImmutable());
    }
    internal HashSet<Guid> OwnedClosure(IEnumerable<Guid> targetIds, CancellationToken ct)
    {
        var targets = targetIds.ToHashSet();
        ExpandOwnedClosure(targets, ct);
        return targets;
    }

    private void ExpandOwnedClosure(HashSet<Guid> targets, CancellationToken ct = default)
    {
        var queue = new Queue<Guid>(targets);
        while (queue.TryDequeue(out var parent))
        {
            ct.ThrowIfCancellationRequested();
            foreach (var child in children.GetValueOrDefault(parent, []))
                if (targets.Add(child)) queue.Enqueue(child);
        }
    }

}
