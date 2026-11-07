namespace SIL.Motif.Contract.Responses;

/// <summary>A percent change for an explicitly paired scope, or the reason it cannot be calculated.</summary>
public sealed record TimingCostChange(double? PercentChange, string? UnavailableReason)
{
    /// <summary>Creates an unavailable comparison with a human-readable reason.</summary>
    public static TimingCostChange Unavailable(string reason) => new(null, reason);
}

/// <summary>A word's paired parser work change.</summary>
public sealed record TimingWordCostChange(string Word, TimingCostChange Change);

/// <summary>A parser object's paired work change over the same selected words.</summary>
public sealed record TimingObjectCostChange(string Kind, string Key, string Name, TimingCostChange Change)
{
    /// <summary>Whether the object is paired, new, missing, or lacks comparable detail.</summary>
    public string Status { get; init; } = "unavailable";
    /// <summary>The identity domain used to pair this object.</summary>
    public string IdentityQuality { get; init; } = "unknown";
    /// <summary>The grammar scope required by a local identity.</summary>
    public string? Scope { get; init; }
}

/// <summary>Paired parser work for a scope and its individual words.</summary>
public sealed record TimingCostComparison(TimingCostChange Total, IReadOnlyList<TimingWordCostChange> Words)
{
    /// <summary>Object comparisons where identities and detail are retained.</summary>
    public IReadOnlyList<TimingObjectCostChange> Objects { get; init; } = [];
    /// <summary>Why object-level comparison is unavailable, when no object rows can be compared.</summary>
    public string? ObjectsUnavailableReason { get; init; }
    /// <summary>Creates an unavailable comparison without inventing word results.</summary>
    public static TimingCostComparison Unavailable(string reason) => new(TimingCostChange.Unavailable(reason), []);
}
