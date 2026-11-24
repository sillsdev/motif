namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>The verified point hand-analysis pins are built from; the first call pays the full verification.</summary>
    internal Task<VerifiedPoint> VerifiedPointAsync(CancellationToken ct) => PointAsync(ct);

    /// <summary>
    /// The point a pin serves and how far this handle vouches for it. A store with no trusted certificate has its history
    /// rescanned against the state row first, so a served point is <see cref="StoreVerification.Certified"/> or
    /// <see cref="StoreVerification.Full"/> and never <see cref="StoreVerification.Uncertified"/>. Reading never rewrites the certificate.
    /// <paramref name="stateRead"/> is called once the state row has been read and before the point is loaded.
    /// </summary>
    internal async Task<(VerifiedPoint Point, StoreVerification Verification)> ServedPointAsync(CancellationToken ct,
        Action? stateRead = null, Func<VerifiedPoint, CancellationToken, Task>? materialize = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var onState = stateRead;
            stateRead = null;
            var served = await ReadPublishedAsync(async state =>
            {
                onState?.Invoke();
                var acquired = point is not null && point.State.SamePoint(state) ? point : await AcquireAsync(state, ct);
                if (materialize is not null) await materialize(acquired, ct);
                return acquired;
            }, ct, lazyContent: true);
            if (Verification != StoreVerification.Uncertified) return (served, Verification);
            var replay = await ReadPublishedAsync(async state =>
                state.SamePoint(served.State) ? await ReplayIntegrity.ScanAsync(database, Workers, ct: ct) : null, ct);
            // The store moved between the point and the scan: look again rather than compare unrelated points.
            if (replay is null && attempt < 3) continue;
            if (replay is null) throw new IOException("The store kept changing while its history was being checked.");
            HistoryScans++;
            if (replay.Final() != served.State.ReplayDigest)
                throw new InvalidDataException("Commits or ChangeEntities: the history does not match the stored replay digest.");
            Verification = StoreVerification.Full;
            return (served, StoreVerification.Full);
        }
    }
}
