namespace SIL.Motif.Host.PanGloss;

/// <summary>A per-word PanGloss timing measurement identified independently from its display label.</summary>
/// <param name="Kind">The PanGloss object kind.</param>
/// <param name="Key">The PanGloss object identity key.</param>
/// <param name="IdentityQuality">How PanGloss obtained the object identity.</param>
/// <param name="Direction">The direction in which the parser used the object.</param>
/// <param name="Object">The display label shown for the object.</param>
/// <param name="Word">The exact selected word measured by PanGloss.</param>
/// <param name="Attempts">The supported object attempt count, when available.</param>
/// <param name="Passes">The supported pass count, when available.</param>
/// <param name="ElapsedNs">The recorded self time in nanoseconds, or null when unsupported.</param>
public sealed record PanGlossObjectTiming(
    string Kind, string Key, string IdentityQuality, string Direction, string Object, string Word,
    int? Attempts, int? Passes, long? ElapsedNs)
{
    /// <summary>The recorded self time in milliseconds, or null when PanGloss does not time this direction.</summary>
    public double? ElapsedMs => ElapsedNs is { } elapsed ? elapsed / 1_000_000d : null;
}
