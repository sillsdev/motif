namespace SIL.Motif.Worker.Store;

/// <summary>One per-word timing fact returned by PanGloss for a rule or object.</summary>
public sealed record AssessmentObjectTiming(
    string Kind,
    string Object,
    string Word,
    int? Attempts,
    int? Passes,
    double ElapsedMs);
