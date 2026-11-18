namespace SIL.Motif.RecordStore;

public sealed partial class RecordStore
{
    /// <summary>The verified point hand-analysis pins are built from; the first call pays the full verification.</summary>
    internal Task<VerifiedPoint> VerifiedPointAsync(CancellationToken ct) => PointAsync(ct);
}
