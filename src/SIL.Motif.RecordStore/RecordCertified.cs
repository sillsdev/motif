using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.RecordStore.HandAnalysis;

namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>Test seam: publishes as a build before the provenance invariant did, with no refusal and no limitation.</summary>
    internal bool LegacyProvenanceForTests { get; set; }

    /// <summary>Test hook: called as a certified acquisition reaches each stage (headers, edges, schema); throwing from it stops the acquisition there.</summary>
    internal Action<string>? AcquisitionStage { get; set; }

    /// <summary>Points this handle acquired without decoding the store; each is also counted in <see cref="PointAcquisitions"/>.</summary>
    internal int CertifiedAcquisitions { get; private set; }

    /// <summary>Content rows this handle fetched by id, each checked against its identity, instead of loading every row.</summary>
    internal int DemandedContentRows => content.DemandedCount;

    // The certificate and chunk-sum check passed for this point; a republishing repair clears it.
    private (StoreState State, SetDigest Snapshot)? derivedChecked;

    /// <summary>Test hook: false makes every acquisition decode and digest the whole store, as <c>--verify full</c> does.</summary>
    internal bool CertifiedAcquisitionAllowed { get; set; } = true;

    // Why the derived tables cannot be trusted, or null: binding, certificate and chunk sum, once per point.
    private async Task<string?> DerivedFindingAsync(StoreState state, CancellationToken ct) => (await CheckDerivedAsync(state, ct)).Finding;

    private async Task<(string? Finding, SetDigest Snapshot)> CheckDerivedAsync(StoreState state, CancellationToken ct)
    {
        if (derivedChecked is { } done && done.State.SamePoint(state)) return (null, done.Snapshot);
        try
        {
            var binding = await ReadBindingAsync(ct);
            if (binding is null) return ("RecordPublicationBinding: the binding row is missing.", default);
            var (revision, snapshot, replay, contentDigest, index, _) = binding.Value;
            if (revision != state.Revision || snapshot != state.SnapshotDigest || replay != state.ReplayDigest ||
                contentDigest != state.ContentDigest || index != state.IndexDigest)
                return ("RecordPublicationBinding: the binding row does not match RecordStoreState; run RebuildReferenceIndexAsync (store-rebuild-index) to rewrite the derived tables.", default);
            var certificate = await CertificateAsync(ct);
            if (certificate is null || !Trusted(certificate, state, await StoreIdentityAsync(ct), ignoreFlag: true))
                return ("RecordStoreCertificate: it is missing or does not match RecordStoreState and this store file.", default);
            var total = Total((await ReadChunkRowsAsync(ct)).Values);
            if (total.Final("snapshot") != state.SnapshotDigest)
                return ("RecordChunks: the chunk digests do not sum to the stored snapshot digest; run RebuildReferenceIndexAsync (store-rebuild-index) to rewrite the derived tables.", default);
            derivedChecked = (state, total);
            return (null, total);
        }
        catch (Microsoft.Data.Sqlite.SqliteException failure) { return ("Derived tables: " + failure.Message, default); }
        catch (InvalidDataException failure) { return (failure.Message, default); }
    }

    // Keyed reads are served only from a point whose binding, certificate and chunk sum have been compared.
    private async Task RequireDerivedTrustedAsync(StoreState state, CancellationToken ct)
    {
        await RequireBindingAsync(state, ct);
        if (await DerivedFindingAsync(state, ct) is { } finding)
        {
            // A handle that has seen its derived tables disagree no longer vouches for the store.
            if (Verification == StoreVerification.Certified) Verification = StoreVerification.Uncertified;
            throw new InvalidDataException(finding);
        }
    }

    // One record of any kind from its checked chunk: the cached original, which callers must not change.
    private async Task<GenericRecord?> VerifiedRecordAsync(Guid id, StoreState state, CancellationToken ct) =>
        (await VerifiedChunkAsync(ChunkOf(id), state, ct)).ById.GetValueOrDefault(id);

    // A point from the state row, certificate and binding: flat headers and edges, schema by keyed reads.
    private async Task<VerifiedPoint?> AcquireCertifiedAsync(StoreState state, CancellationToken ct)
    {
        var (_, snapshot) = await CheckDerivedAsync(state, ct);
        AcquisitionStage?.Invoke("headers");
        List<RecordHeader> rows;
        try
        {
            rows = await ReadHeaderRowsAsync(null, null, ct);
            var binding = await ReadBindingAsync(ct);
            if (HeadersDigest(rows).ToText() != binding!.Value.Headers)
                throw new InvalidDataException("RecordHeaders: the header rows do not match the headers digest in RecordPublicationBinding.");
            if (rows.Count(h => !h.Deleted) != snapshot.Count)
                throw new InvalidDataException("RecordHeaders: the live header rows do not match the record count in RecordChunks.");
        }
        catch (Exception failure) when (failure is InvalidDataException or Microsoft.Data.Sqlite.SqliteException)
        {
            // An incomplete or mismatched table is refused, never read as a smaller store.
            throw new IndexUnavailableException(failure.Message, failure, HandAnalysisRefusalFacts.Corrupt);
        }
        var live = rows.Count(h => !h.Deleted);
        var native = new List<ReferenceRecord>(live);
        var byKind = new Dictionary<string, List<Guid>>(StringComparer.Ordinal);
        foreach (var header in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (header.Deleted) continue;
            if (header.Kind == "lcm") native.Add(new(header.Id, header.ClassId, header.Owner, header.OwningField));
            else if (header.Kind is "manifest" or "native-coverage" or "writing-system" or HandAnalysisKinds.Anchor)
            {
                if (!byKind.TryGetValue(header.Kind, out var ids)) byKind[header.Kind] = ids = [];
                ids.Add(header.Id);
            }
        }
        AcquisitionStage?.Invoke("edges");
        var edges = await ReferencePersistence.ReadAsync(database, ct);
        AcquisitionStage?.Invoke("index");
        var projection = new ReferenceProjection(edges, native.ToArray(), state.IndexLimitations.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        // The edge digest decides whether the point is served; a damaged table never reads as empty.
        var accumulated = await Task.Run(() => projection.AccumulateParallel(Workers ?? ParallelWork.DefaultWorkers, ct), ct);
        if (accumulated.Final() != state.IndexDigest) return null;
        AcquisitionStage?.Invoke("schema");
        async Task<GenericRecord[]> Read(string kind)
        {
            var found = new List<GenericRecord>();
            foreach (var id in byKind.GetValueOrDefault(kind) ?? [])
                found.Add(await VerifiedRecordAsync(id, state, ct) ?? throw new InvalidDataException($"RecordHeaders: {kind} record {id} has no stored snapshot."));
            return found.ToArray();
        }
        var schema = NativeUsageSchema.ReadCertified(await Read("manifest"), await Read("native-coverage"), native, ct);
        var projectClass = schema?.ClassId("LangProject");
        var projects = projectClass.HasValue ? native.Where(r => r.ClassId == projectClass).Take(2).ToArray() : [];
        var anchors = await Read(HandAnalysisKinds.Anchor);
        var systems = await Read("writing-system");
        var source = RecordSource.Lazy(state,
            (id, expected, token) => ReadPublishedAsync(async current =>
            {
                RequireSamePoint(current, expected);
                return await VerifiedRecordAsync(id, current, token) is { Kind: "lcm" } record ? record : null;
            }, token, lazyContent: true),
            (ids, expected, token) => ReadPublishedAsync(async current =>
            {
                RequireSamePoint(current, expected);
                var records = new GenericRecord?[ids.Count];
                for (var i = 0; i < ids.Count; i++)
                    records[i] = await VerifiedRecordAsync(ids[i], current, token) is { Kind: "lcm" } record ? record : null;
                return records;
            }, token, lazyContent: true),
            (expected, token) => ReadPublishedAsync(async current =>
            {
                RequireSamePoint(current, expected);
                var decoded = await DecodeLatestAsync(digest: true, token);
                VerifySnapshots(current, decoded.Digest);
                var all = decoded.Records.Where(r => r.Kind == "lcm").ToDictionary(r => r.Id);
                try { schema?.RequireProvenance(all.Values, token); }
                catch (InvalidDataException) { throw new IndexUnavailableException("The native schema proof is unavailable."); }
                return all;
            }, token));
        ct.ThrowIfCancellationRequested();
        var verified = new VerifiedPoint(state, source,
            new ReferenceIndexSnapshot(state.Revision, projection.Edges, projection.Records, projection.Limitations, schema), snapshot, accumulated)
        {
            Anchors = anchors,
            WritingSystems = systems,
            ProjectId = projects.Length == 1 ? projects[0].Id : null
        };
        PointAcquisitions++;
        CertifiedAcquisitions++;
        Verification = StoreVerification.Certified;
        point = verified;
        return verified;
    }

    private static void RequireSamePoint(StoreState current, StoreState expected)
    {
        if (!current.SamePoint(expected))
            throw new StoreRevisionUnavailableException();
    }
}
