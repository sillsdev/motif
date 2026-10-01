namespace SIL.Motif.Worker.Store;

/// <summary>One per-word timing fact returned by PanGloss for a rule or object.</summary>
public sealed record AssessmentObjectTiming(
    string Kind,
    string Key,
    string IdentityQuality,
    string Direction,
    string Object,
    string Word,
    int? Attempts,
    int? Passes,
    long? ElapsedNs)
{
    /// <summary>The recorded self time in milliseconds, or null when that kind and direction are not timed.</summary>
    public double? ElapsedMs => ElapsedNs is { } elapsed ? elapsed / 1_000_000d : null;
}
