using System.Collections.Immutable;

namespace SIL.Motif.RecordStore;

public sealed record NativeReference(Guid Source, int Field, int Position, Guid Target, bool Owning, string Occurrence = "");
public sealed record ReferenceRecord(Guid Id, int ClassId, Guid? Owner, int OwningField);
public enum ReferenceWitnessKind { Anchor, ConflictingHead, ParserReading }
public sealed record ReferenceWitness(ReferenceWitnessKind Kind, string Identity, string EvidenceRevision,
    Guid Target, Guid? NativeSource = null);
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
    private readonly ImmutableDictionary<Guid, ImmutableArray<NativeReference>> incoming;
    private readonly ImmutableDictionary<Guid, ImmutableArray<Guid>> children;
    private readonly ImmutableDictionary<Guid, ReferenceRecord> records;
    private readonly ImmutableArray<string> limitations;
    private readonly ImmutableArray<ReferenceWitness> witnesses;
    private readonly bool nativeClosureComplete;
    public string Revision { get; }

    internal ReferenceIndexSnapshot(string revision, IEnumerable<NativeReference> edges,
        IEnumerable<ReferenceRecord> records, IEnumerable<string> limitations, ReferenceEvidence? evidence, NativeUsageSchema? usageSchema = null)
    {
        Revision = revision;
        this.usageSchema = usageSchema;
        var materialized = edges.ToArray();
        incoming = materialized.GroupBy(e => e.Target).ToImmutableDictionary(g => g.Key, g => g.ToImmutableArray());
        children = materialized.Where(e => e.Owning).GroupBy(e => e.Source)
            .ToImmutableDictionary(g => g.Key, g => g.Select(e => e.Target).ToImmutableArray());
        this.records = records.ToImmutableDictionary(r => r.Id);
        var nativeLimitations = limitations.ToArray();
        nativeClosureComplete = nativeLimitations.Length == 0;
        var missing = nativeLimitations.ToList();
        if (evidence is not null && evidence.StoreRevision != revision)
            throw new InvalidOperationException("Reference evidence belongs to another store revision.");
        // Caller observations cannot establish that an external domain has been exhaustively indexed.
        missing.Add("Anchor coverage is unavailable.");
        missing.Add("Conflicting-head coverage is unavailable.");
        missing.Add("Parser-reference coverage is unavailable.");
        witnesses = evidence?.Witnesses ?? [];
        if (witnesses.IsDefault) throw new ArgumentException("Reference witnesses must be explicit.", nameof(evidence));
        var identities = new HashSet<(ReferenceWitnessKind, string)>();
        foreach (var witness in witnesses)
        {
            var expected = witness.Kind switch
            {
                ReferenceWitnessKind.Anchor => evidence?.AnchorRevision,
                ReferenceWitnessKind.ConflictingHead => evidence?.ConflictRevision,
                ReferenceWitnessKind.ParserReading => evidence?.ParserRevision,
                _ => null
            };
            if (expected is null || expected != witness.EvidenceRevision || string.IsNullOrWhiteSpace(witness.Identity) ||
                !identities.Add((witness.Kind, witness.Identity))) missing.Add("Unknown, duplicate or stale external witness.");
            if (!this.records.ContainsKey(witness.Target)) missing.Add("External witness target is unavailable.");
            if (witness.NativeSource.HasValue && !this.records.ContainsKey(witness.NativeSource.Value))
                missing.Add("External witness has an unknown native source.");
        }
        this.limitations = missing.Distinct(StringComparer.Ordinal).ToImmutableArray();
    }

    public ReferenceRecord? Record(Guid id) => records.GetValueOrDefault(id);

    public ReferenceResult Incoming(IEnumerable<Guid> targetIds, bool includeDescendants = true)
    {
        var targets = targetIds.ToHashSet();
        var missing = limitations.ToBuilder();
        if (!includeDescendants) missing.Add("Owning descendants were excluded from this query.");
        var knownTargets = targets.All(records.ContainsKey);
        if (!knownTargets) missing.Add("A requested native target is unavailable.");
        if (includeDescendants)
        {
            var queue = new Queue<Guid>(targets);
            while (queue.TryDequeue(out var parent))
                foreach (var child in children.GetValueOrDefault(parent, []))
                    if (targets.Add(child)) queue.Enqueue(child);
        }
        var native = targets.SelectMany(id => incoming.GetValueOrDefault(id, [])).OrderBy(e => e.Target)
            .ThenBy(e => e.Source).ThenBy(e => e.Field).ThenBy(e => e.Position).ThenBy(e => e.Occurrence, StringComparer.Ordinal).ToImmutableArray();
        var external = witnesses.Where(w => targets.Contains(w.Target)).OrderBy(w => w.Kind)
            .ThenBy(w => w.Identity, StringComparer.Ordinal).ToImmutableArray();
        return new ReferenceResult(Revision, targets.Order().ToImmutableArray(), native, external,
            nativeClosureComplete && includeDescendants && knownTargets, missing.ToImmutable());
    }
}
