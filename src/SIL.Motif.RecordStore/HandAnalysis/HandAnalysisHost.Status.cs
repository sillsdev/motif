using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>The steps of opening a host and of loading a point, in the order they run.</summary>
internal enum OpenPhase { Open, State, Load, Project, Pin }

/// <summary>Opening: the handle is being created. Opened: no point is loaded yet. Verifying: a point is being loaded. Ready: a point is served. Unavailable: the last load was refused or the host is disposed.</summary>
internal enum HostReadiness { Opening, Opened, Verifying, Ready, Unavailable }

/// <summary>A query that needs the reference index waits for Building and is refused for Unavailable; it never reads an empty index.</summary>
internal enum ReferenceIndexState { NotBuilt, Building, Ready, Unavailable }

internal sealed record PhaseTiming(OpenPhase Phase, TimeSpan Start, TimeSpan Duration);

/// <summary>Findings are table-named lines; <see cref="Passed"/> means every digest agreed and the point was re-certified.</summary>
internal sealed record HostVerification(bool Passed, IReadOnlyList<string> Findings);

internal sealed record HostStatus(HostReadiness Readiness, ReferenceIndexState ReferenceIndex, SnapshotStamp? Current,
    IReadOnlyList<PhaseTiming> Phases, IReadOnlyList<TableState> Tables, int RetainedPoints, int HeldPins,
    DateTimeOffset? LastFullVerification, string LastFullVerificationBy, string? Unavailable);

internal sealed partial class HandAnalysisHost
{
    private volatile HostReadiness readiness = HostReadiness.Opening;
    private volatile ReferenceIndexState referenceIndex = ReferenceIndexState.NotBuilt;
    private volatile PhaseTiming[] phases = [];
    private volatile string? unavailable;
    private StoreState? verifiedProof;
    private DateTimeOffset? lastVerifiedAt;
    private string lastVerifiedBy = "";

    public HostReadiness Readiness => readiness;

    /// <summary>The timings of the last completed open or load, in phase order; starts are offsets from the host's creation.</summary>
    public IReadOnlyList<PhaseTiming> Phases => phases;

    /// <summary>Test hook: called with the short-lived read-write handle that <see cref="VerifyAsync"/> and <see cref="RebuildIndexAsync"/> open.</summary>
    internal Action<RecordStore>? ShortLivedStoreOpened { get; set; }

    private sealed class PhaseRecorder(HandAnalysisHost host)
    {
        private readonly List<PhaseTiming> done = [..host.phases.Where(p => p.Phase == OpenPhase.Open)];
        private OpenPhase? current;
        private TimeSpan start;

        public void Begin(OpenPhase phase, CancellationToken ct)
        {
            Close();
            current = phase;
            start = host.clock.Elapsed;
            host.progress?.Report(phase);
            ct.ThrowIfCancellationRequested();
        }

        private void Close()
        {
            if (current is { } phase) done.Add(new(phase, start, host.clock.Elapsed - start));
            current = null;
        }

        public void Finish()
        {
            Close();
            host.phases = done.ToArray();
        }

        public void Abandon() => current = null;
    }

    /// <summary>Where the host stands: readiness, the point served, per-phase timings and every table's state. It never waits for a load.</summary>
    public async Task<HostStatus> StatusAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        PinnedPoint? head;
        int held, retained;
        // Status never waits for a load, so it takes only the lock that guards the ring and the held pins.
        lock (pinLifetime)
        {
            head = ring.First?.Value;
            retained = ring.Count;
            held = live.Values.Sum(entry => entry.Count);
        }
        IReadOnlyList<TableState> tables;
        try { tables = await Task.Run(() => RecordStore.InspectTables(path), ct); }
        catch (Exception exception) when (exception is Microsoft.Data.Sqlite.SqliteException or IOException)
        { tables = [new("RecordStoreState", "unreadable", exception.Message)]; }
        // A pinned point passed the index check, so its table is trusted; without one the check has not run.
        tables = tables.Select(t => t.Table == "IncomingReferences" && head is not null
            ? t with { State = "trusted", Detail = t.Detail + "; digest checked when the point loaded" } : t).ToArray();
        var by = lastVerifiedAt is null
            ? tables.FirstOrDefault(t => t.Table == "RecordStoreCertificate")?.Detail ?? ""
            : lastVerifiedBy;
        return new(readiness, referenceIndex, head?.Stamp, phases, tables, retained, held, lastVerifiedAt, by, unavailable);
    }

    /// <summary>
    /// Verifies the whole store: a short-lived read-write handle rescans history and snapshots without the writer lock
    /// and takes the lock only to re-certify the point it verified. Damage is returned as findings that name the table;
    /// a store that changed meanwhile is an <see cref="InvalidOperationException"/> to retry.
    /// </summary>
    internal async Task<HostVerification> VerifyAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        StoreState proof;
        try
        {
            await using var store = await Task.Run(() => new RecordStore(path), ct);
            ShortLivedStoreOpened?.Invoke(store);
            proof = await store.VerifyCertifyingAsync(ct);
        }
        catch (InvalidDataException failure)
        {
            const string prefix = "Store verification failed. ";
            var text = failure.Message.StartsWith(prefix, StringComparison.Ordinal) ? failure.Message[prefix.Length..] : failure.Message;
            return new(false, text.Split("; ", StringSplitOptions.RemoveEmptyEntries));
        }
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            verifiedProof = proof;
            lastVerifiedAt = DateTimeOffset.UtcNow;
            lastVerifiedBy = "this host";
            lock (pinLifetime)
                for (var node = ring.First; node is not null; node = node.Next)
                    if (node.Value.Proof.SamePoint(proof) && node.Value.Stamp.Verification != SnapshotVerification.Full)
                        node.Value = node.Value with { Stamp = node.Value.Stamp with { Verification = SnapshotVerification.Full } };
        }
        finally { gate.Release(); }
        return new(true, []);
    }

    /// <summary>Rebuilds the reference index from verified snapshots through a short-lived read-write handle, then reloads the current point.</summary>
    internal async Task<SnapshotStamp> RebuildIndexAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        await using (var store = await Task.Run(() => new RecordStore(path), ct))
        {
            ShortLivedStoreOpened?.Invoke(store);
            await store.RebuildReferenceIndexAsync(ct);
        }
        return await RefreshAsync(ct);
    }
}
