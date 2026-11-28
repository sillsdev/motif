using SIL.Motif.RecordStore.HandAnalysis;
using System.Security.Cryptography;
using System.Text.Json;

namespace SIL.Motif.RecordStore;

internal sealed class StoreState
{
    public int Id { get; set; } = 1;
    public bool Published { get; set; }
    public string Revision { get; set; } = "";
    public string ReplayDigest { get; set; } = "";
    public string SnapshotDigest { get; set; } = "";
    public string ContentDigest { get; set; } = "";
    public string IndexDigest { get; set; } = "";
    public string IndexLimitations { get; set; } = "";

    public bool SamePoint(StoreState other) => Published == other.Published && Revision == other.Revision &&
        ReplayDigest == other.ReplayDigest && SnapshotDigest == other.SnapshotDigest && ContentDigest == other.ContentDigest &&
        IndexDigest == other.IndexDigest && IndexLimitations == other.IndexLimitations;
}

/// <summary>
/// A published point retained by one handle: the native records, the reference snapshot and the
/// incrementally updatable digests that bind them to the stored state row. History is scanned lazily, once.
/// A certified point has read only headers, edges and a few schema records.
/// </summary>
internal sealed record VerifiedPoint(StoreState State, RecordSource Records, ReferenceIndexSnapshot Index,
    SetDigest Snapshot, IndexDigest IndexDigest)
{
    public ReplayState? Replay { get; init; }
    /// <summary>Chunk digests and headers from the decode that built this point; kept for verification, which compares them with the stored tables.</summary>
    internal DerivedSnapshot? Derived { get; init; }
    public bool IndexTrusted { get; init; } = true;
    public Guid? ProjectId { get; init; }
    public GenericRecord[] Anchors { get; init; } = [];
    public GenericRecord[] WritingSystems { get; init; } = [];
    private ReferenceIndexSnapshot? planningIndex;
    public async Task<ReferenceIndexSnapshot> GetPlanningIndexAsync(CancellationToken ct = default) =>
        planningIndex ??= await HandAnalysisReferences.WithAnchorsAsync(Index, Records, Anchors, ct);
}

internal sealed record ReferenceProjection(NativeReference[] Edges, ReferenceRecord[] Records, string[] Limitations)
{
    public static ReferenceProjection Build(IEnumerable<GenericRecord> input)
    {
        var all = input.ToArray();
        var records = all.Where(r => r.Kind == "lcm").ToDictionary(r => r.Id);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        if (all.Any(r => r.Kind is not ("lcm" or "manifest" or "writing-system" or "resource" or "native-coverage" or
            HandAnalysisKinds.Anchor or HandAnalysisKinds.Request)))
            missing.Add("An unknown record namespace may contain references.");
        foreach (var anchor in all.Where(r => r.Kind == HandAnalysisKinds.Anchor))
            if (HandAnalysisKinds.AnchorTarget(anchor) is not { } at || !records.ContainsKey(at))
                missing.Add("A hand-anchor record is malformed or targets an unknown native record.");
        ValidateCoverage(all, records.Values, missing);
        if (NativeUsageSchema.ProvenanceFailure(all) is { } unproven)
            missing.Add($"{NativeUsageSchema.ProvenanceLimitation}: record {unproven:D}.");
        var edges = new List<NativeReference>();
        var project = all.FirstOrDefault(r => r.Kind == "manifest")?.Values.GetValueOrDefault("nativeProject");
        var projectName = project?.ValueKind == JsonValueKind.String ? project.Value.GetString() : null;
        foreach (var source in records.Values)
        foreach (var (field, value) in source.Values)
        {
            var type = RecordInitialization.Type(source, field);
            if (type is not (1 or 2 or 5 or 6 or 8 or 9 or 13 or 14 or 15 or 16 or 23 or 24 or 25 or 26 or 27 or 28))
            { missing.Add("Native field metadata is unavailable or unsupported."); continue; }
            if (type is 9 or 13 or 14 or 16)
                NativeTextReferences.Add(source, field, type, value, projectName, edges, missing);
            if (type is not (23 or 24 or 25 or 26 or 27 or 28)) continue;
            var id = int.Parse(field, System.Globalization.CultureInfo.InvariantCulture);
            var owning = type is 23 or 25 or 27;
            if (type is 23 or 24)
            {
                if (value.ValueKind != JsonValueKind.Null) edges.Add(new(source.Id, id, -1, value.GetGuid(), owning));
            }
            else
            {
                var items = value.EnumerateArray().Select(v => v.GetGuid()).ToArray();
                // The import vector is stale once a list operation has committed on this field.
                var authored = source.Lists.TryGetValue(id, out var history) && history.Ops.Count > 0;
                if (!authored && source.Provenance?.Vectors.TryGetValue(id, out var native) == true)
                {
                    var matches = type is 27 or 28
                        ? items.AsSpan().SequenceEqual(native.AsSpan())
                        : items.Order().SequenceEqual(native.Order());
                    if (!matches)
                        missing.Add("Native traversal does not match the retained graph.");
                    else items = native.ToArray();
                }
                for (var position = 0; position < items.Length; position++) edges.Add(new(source.Id, id, position, items[position], owning));
            }
        }
        var ownership = edges.Where(e => e.Owning).GroupBy(e => e.Target).ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var edge in edges)
        {
            if (!records.TryGetValue(edge.Target, out var target)) { missing.Add("A native reference target is unavailable."); continue; }
            if (edge.Owning && (target.Owner != edge.Source || target.OwningField != edge.Field))
                missing.Add("Native ownership does not match its owning field.");
        }
        foreach (var record in records.Values)
        {
            var owners = ownership.GetValueOrDefault(record.Id, []);
            if (record.Owner.HasValue ? owners.Length != 1 || owners[0].Source != record.Owner : owners.Length != 0 || record.OwningField != 0)
                missing.Add("Native ownership closure is incomplete.");
            var seen = new HashSet<Guid>();
            var current = record;
            while (current.Owner.HasValue)
            {
                if (!seen.Add(current.Id)) { missing.Add("Native ownership has a cycle."); break; }
                if (!records.TryGetValue(current.Owner.Value, out current!)) { missing.Add("A native owner is unavailable."); break; }
            }
        }
        return new(edges.ToArray(), records.Values.Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)).ToArray(),
            missing.Order(StringComparer.Ordinal).ToArray());
    }

    private static void ValidateCoverage(GenericRecord[] all, IEnumerable<GenericRecord> records, HashSet<string> missing)
    {
        var basis = all.Where(r => r.Kind == "native-coverage").ToArray();
        if (basis.Length != 1 || !basis[0].Values.TryGetValue("basis", out var encoded))
        { missing.Add("Complete native import coverage is unavailable."); return; }
        NativeImportCoverage? coverage;
        try { coverage = encoded.Deserialize<NativeImportCoverage>(); }
        catch (JsonException) { missing.Add("Native import coverage is malformed."); return; }
        if (coverage is null || coverage.Version != 1 || string.IsNullOrWhiteSpace(coverage.AdapterVersion) ||
            coverage.Layouts is null || coverage.ClassCounts is null)
        { missing.Add("Native import coverage is unsupported."); return; }
        var materialized = records.ToArray();
        var headers = materialized.Select(r => new ReferenceRecord(r.Id, r.ClassId, r.Owner, r.OwningField)).ToArray();
        if (coverage.IdentityDigest != NativeImportCoverage.Identities(headers) ||
            !coverage.ClassCounts.OrderBy(p => p.Key).SequenceEqual(materialized.GroupBy(r => r.ClassId)
                .Select(g => new KeyValuePair<int, int>(g.Key, g.Count())).OrderBy(p => p.Key)))
            missing.Add("Native import membership does not match its source coverage.");
        var fieldsByClass = coverage.Layouts.Where(p => p.Value is not null).ToDictionary(p => p.Key, p =>
            p.Value.Select(field => (Key: field.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), Type: field.Value)).ToArray());
        foreach (var record in materialized)
        {
            if (!fieldsByClass.TryGetValue(record.ClassId, out var fields) || record.Values.Count != fields.Length ||
                fields.Any(field => !record.Values.ContainsKey(field.Key) || RecordInitialization.Type(record, field.Key) != field.Type))
                missing.Add("Native fields do not match the complete source layout.");
        }
    }

    public IndexDigest Accumulate(CancellationToken ct = default)
    {
        var records = new SetDigest();
        foreach (var record in Records)
        {
            ct.ThrowIfCancellationRequested();
            records = records.Add(IndexDigest.Item(record));
        }
        var edges = new SetDigest();
        foreach (var edge in Edges)
        {
            ct.ThrowIfCancellationRequested();
            edges = edges.Add(IndexDigest.Item(edge));
        }
        return new IndexDigest(records, edges, Limitations.Order(StringComparer.Ordinal).ToArray());
    }

    /// <summary>The same accumulators as <see cref="Accumulate"/>, hashed on worker threads; sums are order-independent.</summary>
    public IndexDigest AccumulateParallel(int workers, CancellationToken ct = default)
    {
        const int Slice = 32768;
        var recordSlices = (Records.Length + Slice - 1) / Slice;
        var edgeSlices = (Edges.Length + Slice - 1) / Slice;
        var parts = new (SetDigest Records, SetDigest Edges)[recordSlices + edgeSlices];
        ParallelWork.ForEachChunk(parts.Length, workers, ct, slice =>
        {
            ct.ThrowIfCancellationRequested();
            var records = new SetDigest();
            var edges = new SetDigest();
            if (slice < recordSlices)
                for (var i = slice * Slice; i < Math.Min(Records.Length, (slice + 1) * Slice); i++) records = records.Add(IndexDigest.Item(Records[i]));
            else
                for (var i = (slice - recordSlices) * Slice; i < Math.Min(Edges.Length, (slice - recordSlices + 1) * Slice); i++) edges = edges.Add(IndexDigest.Item(Edges[i]));
            parts[slice] = (records, edges);
        });
        var recordsTotal = new SetDigest();
        var edgesTotal = new SetDigest();
        foreach (var (records, edges) in parts) { recordsTotal = recordsTotal.Combine(records); edgesTotal = edgesTotal.Combine(edges); }
        return new IndexDigest(recordsTotal, edgesTotal, Limitations.Order(StringComparer.Ordinal).ToArray());
    }

    public string Digest() => Accumulate().Final();
}

/// <summary>Order-independent, incrementally updatable digest of the published reference index.</summary>
internal sealed record IndexDigest(SetDigest Records, SetDigest Edges, string[] Limitations)
{
    public static byte[] Item(ReferenceRecord record)
    {
        using var output = new BinaryOutput();
        output.Guid(record.Id); output.Integer(record.ClassId); output.Integer(record.OwningField);
        output.Byte(record.Owner.HasValue ? (byte)1 : (byte)0);
        if (record.Owner.HasValue) output.Guid(record.Owner.Value);
        return output.Finish();
    }

    public static byte[] Item(NativeReference edge)
    {
        using var output = new BinaryOutput();
        output.Guid(edge.Target); output.Guid(edge.Source); output.Integer(edge.Field); output.Integer(edge.Position);
        output.Byte(edge.Owning ? (byte)1 : (byte)0); output.Text(edge.Occurrence);
        return output.Finish();
    }

    public IndexDigest WithEdge(NativeReference? removed, NativeReference? added) => this with
    {
        Edges = added is null ? (removed is null ? Edges : Edges.Remove(Item(removed)))
            : removed is null ? Edges.Add(Item(added)) : Edges.Remove(Item(removed)).Add(Item(added))
    };

    public string Final()
    {
        using var output = new BinaryOutput();
        output.Byte(2);
        output.Text(Records.Final("index-records")); output.Text(Edges.Final("index-edges"));
        output.Unsigned((ulong)Limitations.Length);
        foreach (var limitation in Limitations) output.Text(limitation);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(output.Finish()));
    }
}
